#pragma warning disable CS0162 // Unreachable code detected
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using Unity.Mathematics;

public class BSurfaceGcodeGenerator : MonoBehaviour
{
    public BsplineManager _control_point_obj;
    public List<Vector2> _uv_points = new();
    public float _gcode_step_size; // The maximum distance between two 3D points in the gcode (unity units).
    public float _stepover; // The distance that two different parts of the toolpath should be apart from each other in 3D space.
    public float _calculation_step_size; // The distance that is used to calculate the next point with FindStepoverPoint.
    public float _feedrate = 10f; // Feedrate in mm/s.
    private string _file_name;
    private string _file_path;
    public OVRCameraRig _tracking_space;
    void Start()
    {
        _file_name = "BSurface.gcode";
        _file_path = Path.Combine(Application.persistentDataPath, _file_name);
    }
    void OnDrawGizmos()
    {
        if (_uv_points.Count == 0)
            GenerateGcode();
        else
            DrawUVPoints();
        FindStepoverPoint(_debug_target_pos, out _, true); // The _ means I dont care about the out parameter.
    }
    void DrawUVPoints()
    {
        float editor_time_mod1 = (float)EditorApplication.timeSinceStartup % 1;
        // Draw the gcode path in the scene view for debugging.
        // alternate color from white to black for each segment.
        for (int i = 0; i < _uv_points.Count - 1; i++)
        {
            Vector3 start = transform.TransformPoint(_control_point_obj.CalcBsurface(_uv_points[i].x, _uv_points[i].y));
            Vector3 end = transform.TransformPoint(_control_point_obj.CalcBsurface(_uv_points[i + 1].x, _uv_points[i + 1].y));
            bool is_white = Mathf.Floor(editor_time_mod1 * 8) == i % 8;
            Debug.DrawLine(start, end, is_white ? Color.white : Color.black);
        }
    }

    [ContextMenu("GenerateGcode")]
    public void GenerateGcode()
    {
        Start();

        // Check if control points are defined.
        if (_control_point_obj._control_points == null)
        {
            Debug.LogError("Control points are not defined. Please define them first.");
            return;
        }
        // Clear previous uv points.
        _uv_points.Clear();
        // Add start point to uv points.
        _uv_points.Add(new Vector2(0, 0.5f));
        // Traverse circular perimeter of the BSurface.
        for (int i = 0; i < 100; i++)
        {
            float theta = 2 * Mathf.PI * i / 99f;
            // We want to add points going counter clockwise starting from the "major axis index" (conventionally uv=(0,0.5))
            AddPointsToTargetUvExclusive(
                0.5f - 0.5f * Mathf.Cos(theta),
                0.5f - 0.5f * Mathf.Sin(theta)
            );
        }
        // Add final point to uv point.
        _uv_points.Add(new Vector2(0, 0.5f));

        // Draw the uv points in the scene view for debugging.
        DrawUVPoints();
        return;

        // Generate Gcode.
        // Iterate through uv points and add gcode commands to a text file.
        string gcode = "";
        for (int i = 0; i < _uv_points.Count; i++)
        {
            // Get the next gcode position in meters.
            Vector2 target_uv = _uv_points[i];
            Vector3 target_pos = _control_point_obj.CalcBsurface(target_uv.x, target_uv.y);
            // Calculate the normal vector at the target position.
            Vector3 normal = -Vector3.Cross(
                _control_point_obj.CalcBSurfaceVelocityU(target_uv.x, target_uv.y),
                _control_point_obj.CalcBSurfaceVelocityV(target_uv.x, target_uv.y)
            ).normalized;

            // Print velocityU, velocityV, and normal vector for debugging.
            Debug.Log($"VelocityU: {_control_point_obj.CalcBSurfaceVelocityU(target_uv.x, target_uv.y)}");
            Debug.Log($"VelocityV: {_control_point_obj.CalcBSurfaceVelocityV(target_uv.x, target_uv.y)}");
            Debug.Log($"Normal: {normal}");

            // Calculate euler angles for the normal vector.
            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, normal);
            float roll = rotation.eulerAngles.z;
            float pitch = rotation.eulerAngles.x;
            float yaw = rotation.eulerAngles.y;

            // For debug purposes, draw a line representing the normal vector.
            Debug.DrawLine(
                transform.TransformPoint(target_pos),
                transform.TransformPoint(target_pos + normal * 0.01f),
                Color.orange, 10f
            );
            // Also draw du and dv vectors.
            Debug.DrawLine(
                transform.TransformPoint(target_pos),
                transform.TransformPoint(target_pos + _control_point_obj.CalcBSurfaceVelocityU(target_uv.x, target_uv.y).normalized * 0.01f),
                Color.red, 10f
            );
            Debug.DrawLine(
                transform.TransformPoint(target_pos),
                transform.TransformPoint(target_pos + _control_point_obj.CalcBSurfaceVelocityV(target_uv.x, target_uv.y).normalized * 0.01f),
                Color.blue, 10f
            );

            // Add Gcode commands.
            // Linear move to target position.
            gcode += $"G1 X{-1000 * target_pos.x} Y{-1000 * target_pos.z} Z{1000 * target_pos.y} A{roll} B{pitch} C{yaw} F{_feedrate}\n";
        }
        // Write the gcode to a file.
        File.WriteAllText(_file_path, gcode);
        Debug.Log($"BSurface Gcode written to {_file_path} -Calvin");
    }

    /// <summary>
    /// Adds points to the target uv position so that each point is equidistant in 3D space.
    /// Exclusive of the target position.
    /// </summary>
    public void AddPointsToTargetUvExclusive(float u, float v)
    {
        int counter = 0;
        while (true)
        {
            // Exit loop if 1000 loops have been reached.
            if (++counter > 1000)
            {
                Debug.LogError("Infinite loop detected in AddGcodesToTargetUv. Exiting.");
                break;
            }

            // Calculate current uv position.
            Vector2 current_position = _uv_points[^1];
            // Calculate final position.
            Vector2 final_position = new(u, v);

            // Calculate unit direction vector.
            Vector2 uv_direction = (final_position - current_position).normalized;
            // If the uv_direction is zero, we are already at the final position.
            // We want to handle this edge case explicitly to avoid division by zero.
            if (uv_direction == Vector2.zero)
                return;

            // Calculate how the 2D direction affects the 3D direction.
            Vector3 direction_3d =
                uv_direction.x * _control_point_obj.CalcBSurfaceVelocityU(current_position.x, current_position.y) +
                uv_direction.y * _control_point_obj.CalcBSurfaceVelocityV(current_position.x, current_position.y);

            // Divide the uv_direction by the magnitude of the 3D direction vector.
            // This gives us a uv displacement that corresponds to a unit step in 3D space.
            Vector2 scaled_uv_direction = uv_direction / direction_3d.magnitude;

            // Calculate the actual step size in uv space.
            Vector2 uv_step = scaled_uv_direction * _gcode_step_size;

            // Check if the distance to the final position is smaller than the step size.
            if (Vector2.Distance(current_position, final_position) < uv_step.magnitude)
            {
                // If so, exit the loop.
                return;
            }
            // Otherwise, add the current position to the list and update the uv position.
            _uv_points.Add(current_position + uv_step);
        }
    }
    /// <summary>
    /// Given a 3D target position, finds the uv point that corresponds to the 3D point such that: the point is _calculation_step_size distance away from the last point in _uv_points, the point is at least _stepover distance away from the every other point in _uv_points, and the point is closest of its kind to the target position. Function returns the last uv point if all valid solutions are farther away from target position than the last uv point.
    /// Sticky mode only returns extremes, ie. solutions that are next to an invalid solution.
    /// </summary>
    public Vector3 _debug_target_pos;
    public int _angular_resolution_per_rev = 100;
    /// <summary>
    /// A vector of the indices that are less than or equal to _calculation_step_size + _stepover away from the current position.
    /// </summary>
    List<int> sTestworthyIndices;
    Vector2 sPrevUv;
    public enum FindStepoverPointSolutionType
    {
        NoSolution,
        CCWSolution,
        CWSolution,
        CCWFarSolution,
        CWFarSolution
    }
    public Vector2 FindStepoverPoint(Vector3 target_world, out FindStepoverPointSolutionType solution_type, bool sticky_mode = false)
    {
        // If there are no uv points, add the first point.
        if (_uv_points.Count == 0)
        {
            _uv_points.Add(new Vector2(0f, 0.5f));
        }

        // Define variables.
        Vector3 target_pos = _control_point_obj.transform.InverseTransformPoint(target_world);
        Vector2 curr_uv = _uv_points[^1]; // Last index.
        Vector3 curr_pos = _control_point_obj.CalcBsurface(curr_uv.x, curr_uv.y);
        Vector3 curr_world = _control_point_obj.transform.TransformPoint(curr_pos);
        // Vector3 normal = -Vector3.Cross(
        //     _control_point_obj.CalcBSurfaceVelocityU(curr_uv.x, curr_uv.y),
        //     _control_point_obj.CalcBSurfaceVelocityV(curr_uv.x, curr_uv.y)
        // ).normalized;

        // Draw a line from the current point to the target position (in world space).
        Debug.DrawLine(curr_world, target_world, Color.green);

        // Update sPrevUv and store difference in a bool.
        bool curr_uv_changed = sPrevUv != curr_uv;
        sPrevUv = curr_uv;

        // Find the uv direction that moves the closest to the target position.
        // We want to do this analytically, not numerically.
        // Scalar project the vector from the current position to the target position onto velocityU and velocityV vectors.
        // The scalar projection values divided by the magnitude of the corresponding velocity vector tells us the correct direction to move in uv space (I think, at least).
        Vector3 velo_u = _control_point_obj.CalcBSurfaceVelocityU(curr_uv.x, curr_uv.y);
        Vector3 velo_v = _control_point_obj.CalcBSurfaceVelocityV(curr_uv.x, curr_uv.y);
        Vector3 curr_to_target = target_pos - curr_pos;
        // Calculate the scalar projection
        float proj_u = Vector3.Dot(curr_to_target, velo_u) / velo_u.magnitude;
        float proj_v = Vector3.Dot(curr_to_target, velo_v) / velo_v.magnitude;
        // Divide the projections by the magnitude of the corresponding velocity vector.
        float u_dir = proj_u / velo_u.magnitude;
        float v_dir = proj_v / velo_v.magnitude;
        // Create the uv direction vector.
        Vector2 uv_dir_closest = new(u_dir, v_dir);
        // Also store angles for later use.
        float uv_dir_closest_surf_angle = Mathf.Atan2(proj_v, proj_u);
        float uv_dir_closest_uv_angle = Mathf.Atan2(v_dir, u_dir);
        // Print the angles for debugging.
        // Debug.Log($"Angle surface: {uv_dir_closest_surf_angle * Mathf.Rad2Deg}deg, Angle uv: {uv_dir_closest_uv_angle * Mathf.Rad2Deg}deg");

        // If _uv_points is emptyish, reset the testworthy indices.
        // the number 10 is a catch-all, can be reduced probably.
        if (_uv_points.Count < 10)
        {
            sTestworthyIndices = new();
        }
        // If the current uv point changed, or sTestworthyIndices is null, we need to recalculate the testworthy indices.
        if (curr_uv_changed || sTestworthyIndices == null)
        {
            // Initialize the list of testworthy indices.
            sTestworthyIndices = new List<int>();
            // Iterate through the uv points.
            for (int i = 0; i < _uv_points.Count; i++)
            {
                // We already have the current uv's 3D point.
                // Calculate the 3D position of the other uv point.
                Vector2 other_uv = _uv_points[i];
                Vector3 other_pos = _control_point_obj.CalcBsurface(other_uv.x, other_uv.y);
                // Calculate the distance between the curr point and the other point.
                float distance = Vector3.Distance(curr_pos, other_pos);
                // If the distance is {stepover <= dist <= calcstep+stepover}, add the index to the list.
                // >=calc+stepover is too far to be considered, and <=stepover are points that are in our immediate trail.
                // This limits us to instantaneous angle changes of <90deg.
                if (distance >= _stepover && distance <= _calculation_step_size + _stepover)
                {
                    sTestworthyIndices.Add(i);
                }
            }
        }

        // Perform linear search on the two quarters next to the initial guess.
        // Precomputations.
        Vector2 prev_ccw_valid_uv = new();
        Vector2 prev_cw_valid_uv = new();
        for (int i = 0; i <= _angular_resolution_per_rev / 4; i++)
        {
            // Perform 1 CCW and 1 CW shift+test every outerloop, starting with CCW.
            for (int ccw_cw_enum = 0; ccw_cw_enum < 2; ccw_cw_enum++)
            {
                // Define the theta shift in 3d space that we want.
                float theta_shift_wanted = 2 * Mathf.PI * i / _angular_resolution_per_rev;
                if (ccw_cw_enum == 1)
                    theta_shift_wanted = -theta_shift_wanted;

                // Calculate the uv angle shift that corresponds to a 3D angle shift of theta_shift.
                float theta_shift_uv = SurfaceAngleToUVAngle(uv_dir_closest_surf_angle + theta_shift_wanted, velo_u.magnitude, velo_v.magnitude) - uv_dir_closest_uv_angle;

                // Calculate the shifted uv as uv_closest rotated by theta_shift.
                Vector2 uv_dir_shifted = new(
                    Mathf.Cos(theta_shift_uv) * uv_dir_closest.x - Mathf.Sin(theta_shift_uv) * uv_dir_closest.y,
                    Mathf.Sin(theta_shift_uv) * uv_dir_closest.x + Mathf.Cos(theta_shift_uv) * uv_dir_closest.y
                );

                // Renormalize in 3D space.
                // Rescale the uv direction vector by the calculation step size.
                // Do the same for the 3D vector.
                Vector3 uv_dir_3d =
                    uv_dir_shifted.x * _control_point_obj.CalcBSurfaceVelocityU(curr_uv.x, curr_uv.y) +
                    uv_dir_shifted.y * _control_point_obj.CalcBSurfaceVelocityV(curr_uv.x, curr_uv.y);
                uv_dir_shifted /= uv_dir_3d.magnitude;
                uv_dir_shifted *= _calculation_step_size;
                uv_dir_3d = _calculation_step_size * uv_dir_3d.normalized;

                // Check if it is valid.
                Vector2 next_uv = curr_uv + uv_dir_shifted;
                Vector3 next_pos = curr_pos + uv_dir_3d;
                Vector3 next_world = _control_point_obj.transform.TransformPoint(next_pos);
                // Check if next_uv is inside the circle inscribing the BSurface.
                // Check if it is too close to any other point in _uv_points[sTestworthyIndices].
                bool is_valid = (Vector2.Distance(new(0.5f, 0.5f), next_uv) <= 0.5f) && IsValidPos(sTestworthyIndices, next_pos);

                // In sticky mode, we only want to return the point if it is next to an invalid point.
                // If the initial guess is invalid, we want to turn sticky mode off, because the next valid point is gurenteed to be a sticky one.
                if (i == 0 && is_valid == false)
                    sticky_mode = false;
                // Handle sticky mode logic.
                if (sticky_mode == false)
                {
                    if (is_valid == true)
                    {
                        // Print wanted and actual theta shift for debugging.
                        // Debug.Log($"Wanted theta shift: {theta_shift_wanted}, Actual theta shift: {theta_shift_uv}");

                        // Draw a debug line from the current point to the next point in the "shifted" direction.
                        Debug.DrawLine(curr_world, next_world, Color.magenta);
                        solution_type = ccw_cw_enum == 0 ?
                            FindStepoverPointSolutionType.CCWSolution :
                            FindStepoverPointSolutionType.CWSolution;
                        return next_uv;
                    }
                }
                else
                {
                    // We need to find the next invalid point, then return the previous valid point in that direction.
                    if (is_valid == false)
                    {
                        Vector2 output = (ccw_cw_enum == 0) ? prev_ccw_valid_uv : prev_cw_valid_uv;

                        Vector3 output_world = _control_point_obj.transform.TransformPoint(_control_point_obj.CalcBsurface(output.x, output.y));
                        Debug.DrawLine(curr_world, output_world, Color.magenta);
                        solution_type = ccw_cw_enum == 0 ?
                            FindStepoverPointSolutionType.CCWSolution :
                            FindStepoverPointSolutionType.CWSolution;
                        return output;
                    }
                    else
                    {
                        // Update the corresponding previous valid point.
                        if (ccw_cw_enum == 0)
                            prev_ccw_valid_uv = next_uv;
                        else
                            prev_cw_valid_uv = next_uv;
                    }
                }
            }
        }

        // If nothing was returned, return NaN, unless sticky mode is still on, then return curr_uv.
        solution_type = FindStepoverPointSolutionType.NoSolution;
        if (sticky_mode)
            return curr_uv;
        else
            return new Vector2(float.NaN, float.NaN);
    }
    /// <summary>
    /// Checks if the next position is valid by checking if it is at least _stepover distance away from every other point in _uv_points.
    /// </summary>
    bool IsValidPos(in List<int> testworthy_indices, in Vector3 next_pos)
    {
        for (int i_uv = 0; i_uv < testworthy_indices.Count; i_uv++)
        {
            Vector2 other_uv = _uv_points[testworthy_indices[i_uv]];
            // Calculate the 3D position of the other uv point.
            Vector3 other_pos = _control_point_obj.CalcBsurface(other_uv.x, other_uv.y);
            // Calculate the distance between the next point and the other point.
            float distance = Vector3.Distance(next_pos, other_pos);
            // If the distance is less than the stepover distance, the point is not valid.
            if (distance < _stepover)
                return false;
        }
        return true;
    }
    float SurfaceAngleToUVAngle(float surface_angle, float velo_u_mag, float velo_v_mag)
    {
        float sin_theta = Mathf.Sin(surface_angle);
        float cos_theta = Mathf.Cos(surface_angle);
        float2 tan2 = new(cos_theta, sin_theta);
        tan2.x *= velo_v_mag;
        tan2.y *= velo_u_mag;
        return Mathf.Atan2(tan2.y, tan2.x);
    }
}
