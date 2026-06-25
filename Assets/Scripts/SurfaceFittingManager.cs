using System;
using UnityEngine;
using MathNet.Numerics.LinearAlgebra;
using System.Collections.Generic;

public class SurfaceFittingManager : MonoBehaviour
{
    public BsplineManager _bspline;
    public LineRenderer _drawn_loop;
    public float _lambda_regularization = 1;

    [Header("Surface Constraint")]
    public bool _enable_surface_constraint = true;
    public LayerMask _anatomy_layer_mask;
    public ClinicalScenarioManager _scenario_manager;
    public float _projection_offset = 0.5f;
    public float _projection_max_distance = 5.0f;
    public float _min_surface_normal_y = 0.0f;
    public int _loop_closure_anchor_points = 5;

    [Header("Interior Surface Samples")]
    [Tooltip("Grid resolution for sampling the anatomy surface inside the loop (N×N grid, only points inside the unit circle are used). Default 5 gives ~19 interior samples.")]
    public int _interior_sample_resolution = 5;
    [Tooltip("Weight of interior surface samples relative to boundary loop points in the least-squares solve. 1 = equal weight.")]
    public float _interior_sample_weight = 1f;

    Vector3[] _raw_loop_points = null;

    int _major_diameter_index;
    bool _loop_is_clockwise;

    // Samples the anatomy surface on a grid inside the loop boundary.
    // Returns world-space hit points and their corresponding UV coordinates (u,v in [0,1]).
    // Used by both fitting methods to pull the surface interior toward the anatomy.
    private List<(Vector3 point, float u, float v)> SampleInteriorSurfacePoints(float ray_start_y)
    {
        var results = new List<(Vector3, float, float)>();
        if (_scenario_manager == null || !_enable_surface_constraint) return results;

        int res = _interior_sample_resolution;
        Vector2 circle_center = new Vector2(0.5f, 0.5f);

        for (int ui = 0; ui < res; ui++)
        {
            for (int vi = 0; vi < res; vi++)
            {
                // Map grid indices to UV space [0,1].
                float u = (res == 1) ? 0.5f : (float)ui / (res - 1);
                float v = (res == 1) ? 0.5f : (float)vi / (res - 1);

                // Skip points outside the unit circle (the domain of the B-surface).
                if (Vector2.Distance(new Vector2(u, v), circle_center) >= 0.5f) continue;

                // Evaluate the B-surface at this UV to get an approximate world XZ position,
                // then raycast down to find the actual anatomy surface height.
                Vector3 surface_local = _bspline.CalcBsurface(u, v);
                Vector3 surface_world = _bspline.transform.TransformPoint(surface_local);

                Vector3 ray_origin = new Vector3(surface_world.x, ray_start_y, surface_world.z);

                bool prev_backfaces = Physics.queriesHitBackfaces;
                Physics.queriesHitBackfaces = true;
                RaycastHit[] hits = Physics.RaycastAll(ray_origin, Vector3.down, _projection_max_distance, _anatomy_layer_mask);
                Physics.queriesHitBackfaces = prev_backfaces;

                float best_y = float.NegativeInfinity;
                Vector3 best_point = Vector3.zero;
                bool found = false;
                foreach (RaycastHit h in hits)
                {
                    if (h.point.y > best_y) { best_y = h.point.y; best_point = h.point; found = true; }
                }

                if (found)
                    results.Add((best_point, u, v));
            }
        }

        Debug.Log($"SurfaceFittingManager: sampled {results.Count} interior surface points.");
        return results;
    }

    private void ProjectLoopOntoAnatomySurface()
    {
        if (_scenario_manager == null)
        {
            Debug.LogWarning("SurfaceFittingManager: _scenario_manager is not assigned; skipping projection.");
            return;
        }

        int count = _drawn_loop.positionCount;

        // Compute world-space top of the anatomy bounding box so rays always start above it.
        GameObject anatomy = _scenario_manager.GetActiveAnatomy();
        float ray_start_y = _scenario_manager.GetAnatomyCentroid().y + _projection_offset;
        if (anatomy != null)
        {
            MeshFilter mf = anatomy.GetComponent<MeshFilter>();
            if (mf != null && mf.mesh != null)
            {
                Vector3 world_max = anatomy.transform.TransformPoint(mf.mesh.bounds.max);
                ray_start_y = world_max.y + _projection_offset;
            }
        }

        for (int i = 0; i < count; i++)
        {
            Vector3 point = _drawn_loop.GetPosition(i);
            Vector3 ray_origin = new Vector3(point.x, ray_start_y, point.z);

            // Primary: shoot straight down, pick the highest-Y hit (= top surface).
            bool prev_backfaces = Physics.queriesHitBackfaces;
            Physics.queriesHitBackfaces = true;
            RaycastHit[] hits = Physics.RaycastAll(ray_origin, Vector3.down, _projection_max_distance, _anatomy_layer_mask);
            Physics.queriesHitBackfaces = prev_backfaces;
            bool found = false;
            Vector3 best_point = Vector3.zero;
            float best_y = float.NegativeInfinity;
            foreach (RaycastHit h in hits)
            {
                if (h.point.y > best_y) { best_y = h.point.y; best_point = h.point; found = true; }
            }

            // TEMP DEBUG — remove after diagnosing
            if (i == 0) Debug.Log($"Point 0: ray_origin={ray_origin}, hits={hits.Length}, best_y={best_y}, best_point={best_point}");

            if (found)
            {
                _drawn_loop.SetPosition(i, best_point);
            }
            else
            {
                // Fallback: shoot inward toward anatomy centroid (handles points
                // drawn outside the XZ footprint of the mesh, e.g. skull cap rim).
                Vector3 centroid = _scenario_manager.GetAnatomyCentroid();
                Vector3 inward = (centroid - point).normalized;
                Vector3 fallback_origin = point - inward * _projection_offset;
                if (Physics.Raycast(fallback_origin, inward, out RaycastHit fallback_hit, _projection_max_distance, _anatomy_layer_mask))
                    _drawn_loop.SetPosition(i, fallback_hit.point);
                else
                    Debug.LogWarning($"SurfaceFittingManager: No surface hit for loop point {i}; leaving unchanged.");
            }
        }
    }

    [ContextMenu("Fit Elliptical Initial Guess")]
    public void FitSurfaceToDrawingEllipticalHeuristic()
    {
        // Capture raw loop on first call, restore it on subsequent calls.
        if (_raw_loop_points == null || _raw_loop_points.Length == 0)
        {
            _raw_loop_points = new Vector3[_drawn_loop.positionCount];
            for (int i = 0; i < _drawn_loop.positionCount; i++)
                _raw_loop_points[i] = _drawn_loop.GetPosition(i);
        }
        else
        {
            _drawn_loop.positionCount = _raw_loop_points.Length;
            for (int i = 0; i < _raw_loop_points.Length; i++)
                _drawn_loop.SetPosition(i, _raw_loop_points[i]);
        }

        if (_enable_surface_constraint)
            ProjectLoopOntoAnatomySurface();

        // Exit if there are less than 10 points in the drawn loop.
        if (_drawn_loop.positionCount < 10)
            return;

        const float resolution = 0.01f;
        int num_points = _drawn_loop.positionCount;
        int n = Mathf.Min(_loop_closure_anchor_points, num_points / 4); // Safety clamp.

        // Compute anchor positions for tangent estimation only.
        Vector3 start_anchor = Vector3.zero;
        for (int i = 0; i < n; i++)
            start_anchor += _drawn_loop.GetPosition(i);
        start_anchor /= n;

        Vector3 end_anchor = Vector3.zero;
        for (int i = num_points - n; i < num_points; i++)
            end_anchor += _drawn_loop.GetPosition(i);
        end_anchor /= n;

        // Use the actual trimmed boundary points as bridge endpoints to avoid gaps.
        Vector3 bridge_start = _drawn_loop.GetPosition(num_points - n - 1); // Last kept point.
        Vector3 bridge_end = _drawn_loop.GetPosition(n); // First kept point.

        // Compute tangents from the anchor regions.
        // Outgoing tangent: direction the loop is traveling at the start anchor.
        Vector3 start_tangent = (_drawn_loop.GetPosition(n - 1) - _drawn_loop.GetPosition(0)).normalized;
        // Incoming tangent: direction the loop was traveling as it approached the end anchor.
        Vector3 end_tangent = (_drawn_loop.GetPosition(num_points - 1) - _drawn_loop.GetPosition(num_points - n)).normalized;

        // Trim first N and last N points, keeping only the middle section.
        int trimmed_count = num_points - 2 * n;
        List<Vector3> trimmed_points = new();
        for (int i = n; i < num_points - n; i++)
            trimmed_points.Add(_drawn_loop.GetPosition(i));

        // Generate cubic Hermite bridge from end_anchor to start_anchor.
        // Scale tangents by the distance between anchors for a natural curve.
        float bridge_length = Vector3.Distance(bridge_start, bridge_end);
        Vector3 scaled_end_tangent = end_tangent * bridge_length;
        Vector3 scaled_start_tangent = start_tangent * bridge_length;

        List<Vector3> bridge_points = new();
        int bridge_steps = Mathf.Max(1, Mathf.RoundToInt(bridge_length / resolution));
        for (int i = 0; i <= bridge_steps; i++)
        {
            float t = (float)i / bridge_steps;
            float h00 = 2*t*t*t - 3*t*t + 1;
            float h10 = t*t*t - 2*t*t + t;
            float h01 = -2*t*t*t + 3*t*t;
            float h11 = t*t*t - t*t;
            Vector3 point = h00 * bridge_start + h10 * scaled_end_tangent + h01 * bridge_end + h11 * scaled_start_tangent;
            bridge_points.Add(point);
        }

        // Rebuild the loop: trimmed middle + bridge.
        List<Vector3> final_points = new();
        final_points.AddRange(trimmed_points);
        final_points.AddRange(bridge_points);

        _drawn_loop.positionCount = final_points.Count;
        for (int i = 0; i < final_points.Count; i++)
            _drawn_loop.SetPosition(i, final_points[i]);

        // Find the center of mass of the points.
        num_points = _drawn_loop.positionCount;
        Vector3 center_of_mass = new();
        for (int i = 0; i < num_points; i++)
            center_of_mass += _drawn_loop.GetPosition(i) / num_points;

        // Scan through half the loop, finding the difference between the distances of
        // two pairs of opposite indices, storing the index of the maximum difference.
        float curr_max_difference = 0;
        int curr_max_difference_index = 0;
        for (int i = 0; i < num_points / 2; i++)
        {
            Vector3 no_rev = _drawn_loop.GetPosition(i);
            Vector3 quarter_rev = _drawn_loop.GetPosition((i + num_points / 4) % num_points);
            Vector3 half_rev = _drawn_loop.GetPosition((i + num_points / 2) % num_points);
            Vector3 three_quarter_rev = _drawn_loop.GetPosition((i + 3 * num_points / 4) % num_points);
            float major_dist = Vector3.Distance(no_rev, half_rev);
            float minor_dist = Vector3.Distance(quarter_rev, three_quarter_rev);
            float difference = major_dist - minor_dist;
            if (difference > curr_max_difference)
            {
                curr_max_difference = difference;
                curr_max_difference_index = i;
            }
        }
        _major_diameter_index = curr_max_difference_index;

        // Define vertices of major and minor axes.
        Vector3 major_axis = _drawn_loop.GetPosition(_major_diameter_index + num_points / 2) -
            _drawn_loop.GetPosition((_major_diameter_index) % num_points);
        Vector3 minor_axis = _drawn_loop.GetPosition((_major_diameter_index + 3 * num_points / 4) % num_points) -
            _drawn_loop.GetPosition((_major_diameter_index + num_points / 4) % num_points);
        float major_diameter = major_axis.magnitude;
        float minor_diameter = minor_axis.magnitude;
        Vector3 ellipse_normal = Vector3.Cross(major_axis, minor_axis).normalized;
        if (ellipse_normal.y < 0)
        {
            minor_axis = -minor_axis;
            ellipse_normal = Vector3.Cross(major_axis, minor_axis).normalized;
            _loop_is_clockwise = false;
        }
        else
        {
            _loop_is_clockwise = true;
        }
        Quaternion ellipse_rot = Quaternion.LookRotation(-minor_axis, ellipse_normal);
        Quaternion node_ui_rot = transform.rotation;
        Quaternion node_ui_to_ellipse_rot = Quaternion.Inverse(node_ui_rot) * ellipse_rot;
        float ellipse_scale = 1f / 4f;

        for (int i = 0; i < _bspline._control_points.GetLength(0); i++)
            for (int j = 0; j < _bspline._control_points.GetLength(1); j++)
            {
                float unit_i = 1f * i / (_bspline._control_points.GetLength(0) - 1);
                float unit_j = 1f * j / (_bspline._control_points.GetLength(1) - 1);
                _bspline._control_points[i, j] = new Vector3(
                    ellipse_scale * (major_diameter * unit_i - major_diameter / 2),
                    0,
                    ellipse_scale * (minor_diameter * unit_j - minor_diameter / 2)
                );
                _bspline._control_points[i, j] = node_ui_to_ellipse_rot * _bspline._control_points[i, j];
                _bspline._control_points[i, j] += _bspline.transform.InverseTransformPoint(center_of_mass);
            }

        // Snap each control point onto the anatomy surface so the initial guess
        // follows the curvature of the mesh, not a flat ellipse.
        if (_enable_surface_constraint && _scenario_manager != null)
        {
            float ray_start_y = _scenario_manager.GetAnatomyCentroid().y + _projection_offset;
            GameObject anatomy = _scenario_manager.GetActiveAnatomy();
            if (anatomy != null)
            {
                MeshFilter mf = anatomy.GetComponent<MeshFilter>();
                if (mf != null && mf.mesh != null)
                {
                    Vector3 world_max = anatomy.transform.TransformPoint(mf.mesh.bounds.max);
                    ray_start_y = world_max.y + _projection_offset;
                }
            }

            for (int i = 0; i < _bspline._control_points.GetLength(0); i++)
            {
                for (int j = 0; j < _bspline._control_points.GetLength(1); j++)
                {
                    Vector3 cp_world = _bspline.transform.TransformPoint(_bspline._control_points[i, j]);
                    Vector3 ray_origin = new Vector3(cp_world.x, ray_start_y, cp_world.z);

                    bool prev_backfaces = Physics.queriesHitBackfaces;
                    Physics.queriesHitBackfaces = true;
                    RaycastHit[] hits = Physics.RaycastAll(ray_origin, Vector3.down, _projection_max_distance, _anatomy_layer_mask);
                    Physics.queriesHitBackfaces = prev_backfaces;

                    float best_y = float.NegativeInfinity;
                    Vector3 best_point = Vector3.zero;
                    bool found = false;
                    foreach (RaycastHit h in hits)
                    {
                        if (h.point.y > best_y) { best_y = h.point.y; best_point = h.point; found = true; }
                    }

                    if (found)
                        _bspline._control_points[i, j] = _bspline.transform.InverseTransformPoint(best_point);
                }
            }
        }
    }

    [ContextMenu("Fit Least Squares Control Points")]
    public void FitSurfaceToDrawingLeastSquares()
    {
        if (_enable_surface_constraint)
            ProjectLoopOntoAnatomySurface();

        // Exit if there are less than 10 points in the drawn loop.
        if (_drawn_loop.positionCount < 10)
            return;

        // Compute ray start height (needed for interior sampling below).
        float ray_start_y_ls = (_scenario_manager != null)
            ? _scenario_manager.GetAnatomyCentroid().y + _projection_offset
            : 0f;
        if (_scenario_manager != null)
        {
            GameObject anatomy_ls = _scenario_manager.GetActiveAnatomy();
            if (anatomy_ls != null)
            {
                MeshFilter mf_ls = anatomy_ls.GetComponent<MeshFilter>();
                if (mf_ls != null && mf_ls.mesh != null)
                {
                    Vector3 world_max_ls = anatomy_ls.transform.TransformPoint(mf_ls.mesh.bounds.max);
                    ray_start_y_ls = world_max_ls.y + _projection_offset;
                }
            }
        }

        // Sample interior anatomy surface points for curvature constraints.
        var interior_samples = SampleInteriorSurfacePoints(ray_start_y_ls);

        // Create a MathNet vector of points from the drawn loop.
        // We want this vector to start at the point correspondsing to the major diameter index,
        // and then go counter_clockwise around the loop.
        int loop_size = _drawn_loop.positionCount;
        Matrix<float> loop3d = Matrix<float>.Build.Dense(loop_size, 3);
        for (int i = 0; i < loop_size; i++)
        {
            // Calculate the index of the point in the drawn loop that corresponds to the major diameter index.
            int index = (_major_diameter_index + i) % loop_size;
            Vector3 point = _drawn_loop.GetPosition(index);
            loop3d[i, 0] = point.x;
            loop3d[i, 1] = point.y;
            loop3d[i, 2] = point.z;
        }

        // Parameterize the uv circle. Create a matrix of size (loop_size x 2) where n is the number of points in the drawn loop.
        float neg_if_clockwise = _loop_is_clockwise ? -1 : 1; // Make uv circle go clockwise if the loop is clockwise.
        Matrix<float> uv_circle = Matrix<float>.Build.Dense(loop_size, 2);
        for (int i = 0; i < loop_size; i++)
        {
            float theta = 2 * Mathf.PI * i / loop_size;
            float u = 0.5f + 0.5f * -Mathf.Cos(theta);
            float v = 0.5f + 0.5f * -Mathf.Sin(theta) * neg_if_clockwise;
            uv_circle[i, 0] = u;
            uv_circle[i, 1] = v;
        }

        // Precompute the B-spline basis function values for the uv circle (This is our "A" matrix).
        // Store them in a matrix of size (loop_size x num_control_points).
        int num_control_points = _bspline._control_points.GetLength(0) * _bspline._control_points.GetLength(1);
        Matrix<float> basis_values = Matrix<float>.Build.Dense(loop_size, num_control_points);
        for (int i = 0; i < loop_size; i++)
        {
            for (int k = 0; k < num_control_points; k++)
            {
                // Calculate the basis function values for u and v.
                float u = uv_circle[i, 0];
                float v = uv_circle[i, 1];
                int cp_index_u_dir = k % _bspline._control_points.GetLength(0);
                int cp_index_v_dir = k / _bspline._control_points.GetLength(0);
                basis_values[i, k] = _bspline.BasisFunction3D(cp_index_u_dir, cp_index_v_dir, u, v);
            }
        }

        // Append interior surface sample rows to loop3d and basis_values.
        // Each sample contributes one row: its 3D position and its basis values at (u,v).
        // Rows are weighted by _interior_sample_weight so their influence is tunable.
        int interior_count = interior_samples.Count;
        if (interior_count > 0)
        {
            Matrix<float> interior_points = Matrix<float>.Build.Dense(interior_count, 3);
            Matrix<float> interior_basis  = Matrix<float>.Build.Dense(interior_count, num_control_points);
            for (int s = 0; s < interior_count; s++)
            {
                Vector3 pt = interior_samples[s].point;
                float u_s  = interior_samples[s].u;
                float v_s  = interior_samples[s].v;
                interior_points[s, 0] = pt.x * _interior_sample_weight;
                interior_points[s, 1] = pt.y * _interior_sample_weight;
                interior_points[s, 2] = pt.z * _interior_sample_weight;
                for (int k = 0; k < num_control_points; k++)
                {
                    int cp_index_u_dir = k % _bspline._control_points.GetLength(0);
                    int cp_index_v_dir = k / _bspline._control_points.GetLength(0);
                    interior_basis[s, k] = _bspline.BasisFunction3D(cp_index_u_dir, cp_index_v_dir, u_s, v_s)
                                           * _interior_sample_weight;
                }
            }
            loop3d       = loop3d.Stack(interior_points);
            basis_values = basis_values.Stack(interior_basis);
        }

        // Create the regularization matrix
        // We are using initial guess regularization so the "A matrix" for regularization is the identity matrix times lambda.
        Matrix<float> regularization_matrix = Matrix<float>.Build.DenseIdentity(num_control_points) * _lambda_regularization;
        // Create the regularization "b vector" which is the initial guess for the control points times lambda.
        Matrix<float> regularization_b_vector = Matrix<float>.Build.Dense(num_control_points, 3);
        for (int k = 0; k < num_control_points; k++)
        {
            int cp_index_u_dir = k % _bspline._control_points.GetLength(0);
            int cp_index_v_dir = k / _bspline._control_points.GetLength(0);
            Vector3 control_point_global = _bspline.transform.TransformPoint(_bspline._control_points[cp_index_u_dir, cp_index_v_dir]);
            regularization_b_vector[k, 0] = control_point_global.x * _lambda_regularization;
            regularization_b_vector[k, 1] = control_point_global.y * _lambda_regularization;
            regularization_b_vector[k, 2] = control_point_global.z * _lambda_regularization;
        }

        // Append regualarization matrix under the basis values matrix.
        Matrix<float> A = regularization_matrix.Stack(basis_values);
        // Do the same for the b vector.
        Matrix<float> b = regularization_b_vector.Stack(loop3d);

        // Solve the least squares problem Ax = b.
        Matrix<float> x = A.Solve(b);

        // Update the control points with the solution.
        for (int i = 0; i < _bspline._control_points.GetLength(0); i++)
        {
            for (int j = 0; j < _bspline._control_points.GetLength(1); j++)
            {
                int cp_index = j * _bspline._control_points.GetLength(1) + i;
                Vector3 control_point_global = new Vector3(
                    x[cp_index, 0],
                    x[cp_index, 1],
                    x[cp_index, 2]
                );
                _bspline._control_points[i, j] = _bspline.transform.InverseTransformPoint(control_point_global);
            }
        }

        // Debug.DrawLine the loop point corresponding to the first row of the b vector.
        Vector3 loopPoint_0 = new(loop3d[0, 0], loop3d[0, 1], loop3d[0, 2]);
        Debug.DrawLine(loopPoint_0, loopPoint_0 + Vector3.down, Color.black, 10f);
        // Do the same for the point a quarter of the way around the loop.
        Vector3 loopPoint_quarter = new(loop3d[loop_size / 4, 0], loop3d[loop_size / 4, 1], loop3d[loop_size / 4, 2]);
        Debug.DrawLine(loopPoint_quarter, loopPoint_quarter + Vector3.down, Color.red, 10f);
        Debug.Log("Drew 0% and 25% loop points for debugging.");
    }

    public void ClearRawLoop()
    {
        _raw_loop_points = null;
    }

}