#pragma warning disable CS0162 // Unreachable code detected
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using UnityEditor;

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
        FindStepoverPoint(_debug_target_pos);
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
            Vector2 current_position = _uv_points[_uv_points.Count - 1];
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
    /// </summary>
    public Vector3 _debug_target_pos;
    public int _angular_resolution_binary_search = 6;
    /// <summary>
    /// A vector of the indices that are less than or equal to _calculation_step_size + _stepover away from the current position.
    /// </summary>
    List<int> sTestworthyIndices;
    Vector2 sPrevUv;
    public Vector2 FindStepoverPoint(Vector3 target_world)
    {
        // If there are no uv points, add the first point.
        if (_uv_points.Count == 0)
        {
            _uv_points.Add(new Vector2(0f, 0.5f));
        }

        // Define variables.
        Vector3 target_pos = _control_point_obj.transform.InverseTransformPoint(target_world);
        Vector2 curr_uv = _uv_points[_uv_points.Count - 1];
        Vector3 curr_pos = _control_point_obj.CalcBsurface(curr_uv.x, curr_uv.y);
        Vector3 curr_world = _control_point_obj.transform.TransformPoint(curr_pos);

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
        // Normalize the uv direction vector.
        Vector2 uv_dir_closest = new Vector2(u_dir, v_dir).normalized;

        // Normalize the uv direction vector in 3D space.
        Vector3 uv_dir_3d = uv_dir_closest.x * _control_point_obj.CalcBSurfaceVelocityU(curr_uv.x, curr_uv.y) +
                            uv_dir_closest.y * _control_point_obj.CalcBSurfaceVelocityV(curr_uv.x, curr_uv.y);
        uv_dir_closest /= uv_dir_3d.magnitude;
        // Scale the uv direction vector by the step size (corresponds to vec3 of size _calculation_step_size).
        uv_dir_closest *= _calculation_step_size;
        // Scale the 3D direction vector by the step size (is actually of size _calculation_step_size).
        uv_dir_3d = _calculation_step_size * uv_dir_3d.normalized;

        // Define variables.
        Vector2 next_uv = curr_uv + uv_dir_closest;
        Vector3 next_pos = curr_pos + uv_dir_3d;
        Vector3 next_world = _control_point_obj.transform.TransformPoint(next_pos);

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
                // If the distance is less than or equal to the calculation step size + stepover, add the index to the list.
                if (distance <= _calculation_step_size + _stepover)
                {
                    sTestworthyIndices.Add(i);
                }
            }
        }
    
        // Check if next_uv is inside the circle inscribing the BSurface.
        bool is_valid = Vector2.Distance(new(0.5f, 0.5f), next_uv) <= 0.5f;
        // Check if it is too close to any other point in _uv_points.
        is_valid = is_valid && IsValidPos(sTestworthyIndices, next_pos);

        // Draw a line from the current point to the target position (in world space).
        Debug.DrawLine(curr_world, target_world, Color.green);

        // If it isn't too close, return the uv point.
        if (is_valid == true)
        {
            // Draw a line from the current point to the next point in the "closest" direction.
            Debug.DrawLine(curr_world, next_world, Color.magenta);
            // If the next point is further away from the target position than the current point, return the current point.
            if (Vector3.Distance(next_world, target_world) > Vector3.Distance(curr_world, target_world))
                return curr_uv;
            else
                return next_uv;
        }

        // If it is too close, we need to find a valid point that is at least _stepover distance away from every other point in _uv_points.
        // Perform a binary search in the CCW direction.
        float inner_ptr_theta = 0;
        float outer_ptr_theta = Mathf.PI;
        Vector2 last_solution_ccw = new(float.NaN, float.NaN);
        for (int i = 0; i < _angular_resolution_binary_search; i++)
        {
            // Calculate the mid angle between the two pointers.
            float middle_theta = (inner_ptr_theta + outer_ptr_theta) / 2f;
            // Calculate the new uv direction vector as old next_uv rotated by middle_theta.
            Vector2 uv_dir_shifted = new(
                Mathf.Cos(middle_theta) * uv_dir_closest.x - Mathf.Sin(middle_theta) * uv_dir_closest.y,
                Mathf.Sin(middle_theta) * uv_dir_closest.x + Mathf.Cos(middle_theta) * uv_dir_closest.y
            );
            // Renormalize in 3D space.
            Vector3 uv_dir_shifted_3d = uv_dir_shifted.x * _control_point_obj.CalcBSurfaceVelocityU(curr_uv.x, curr_uv.y) +
                                        uv_dir_shifted.y * _control_point_obj.CalcBSurfaceVelocityV(curr_uv.x, curr_uv.y);
            uv_dir_shifted /= uv_dir_shifted_3d.magnitude;
            // Rescale the uv direction vector by the calculation step size.
            uv_dir_shifted *= _calculation_step_size;
            // Do the same for the 3D vector.
            uv_dir_shifted_3d = _calculation_step_size * uv_dir_shifted_3d.normalized;

            // Check if it is valid.
            next_uv = curr_uv + uv_dir_shifted;
            next_pos = curr_pos + uv_dir_shifted_3d;
            // Check if next_uv is inside the circle inscribing the BSurface.
            // Check if it is too close to any other point in _uv_points[testworthy_indices].
            bool is_valid_bin_search = Vector2.Distance(new(0.5f, 0.5f), next_uv) <= 0.5f;
            is_valid_bin_search = is_valid_bin_search && IsValidPos(sTestworthyIndices, next_pos);
            // If valid, update last_solution.
            if (is_valid_bin_search)
                last_solution_ccw = next_uv;
            // Change the values of the pointers according to is_valid.
            if (is_valid_bin_search)
            {
                // If it is valid, move the outer pointer to the middle angle.
                outer_ptr_theta = middle_theta;
            }
            else
            {
                // If it is not valid, move the inner pointer to the middle angle.
                inner_ptr_theta = middle_theta;
            }
        }
        // Do the same for the CW direction.
        inner_ptr_theta = 0;
        outer_ptr_theta = -Mathf.PI;
        Vector2 last_solution_cw = new(float.NaN, float.NaN);
        for (int i = 0; i < _angular_resolution_binary_search; i++)
        {
            // Calculate the mid angle between the two pointers.
            float middle_theta = (inner_ptr_theta + outer_ptr_theta) / 2f;
            // Calculate the new uv direction vector as old next_uv rotated by middle_theta.
            Vector2 uv_dir_shifted = new(
                Mathf.Cos(middle_theta) * uv_dir_closest.x - Mathf.Sin(middle_theta) * uv_dir_closest.y,
                Mathf.Sin(middle_theta) * uv_dir_closest.x + Mathf.Cos(middle_theta) * uv_dir_closest.y
            );
            // Renormalize in 3D space.
            Vector3 uv_dir_shifted_3d = uv_dir_shifted.x * _control_point_obj.CalcBSurfaceVelocityU(curr_uv.x, curr_uv.y) +
                                        uv_dir_shifted.y * _control_point_obj.CalcBSurfaceVelocityV(curr_uv.x, curr_uv.y);
            uv_dir_shifted /= uv_dir_shifted_3d.magnitude;
            // Rescale the uv direction vector by the calculation step size.
            uv_dir_shifted *= _calculation_step_size;
            // Do the same for the 3D vector.
            uv_dir_shifted_3d = _calculation_step_size * uv_dir_shifted_3d.normalized;

            // Check if it is valid.
            next_uv = curr_uv + uv_dir_shifted;
            next_pos = curr_pos + uv_dir_shifted_3d;
            // Check if next_uv is inside the circle inscribing the BSurface.
            // Check if it is too close to any other point in _uv_points[sTestworthyIndices].
            bool is_valid_bin_search = Vector2.Distance(new(0.5f, 0.5f), next_uv) <= 0.5f;
            is_valid_bin_search = is_valid_bin_search && IsValidPos(sTestworthyIndices, next_pos);
            // If valid, update last_solution.
            if (is_valid_bin_search)
                last_solution_cw = next_uv;
            // Change the values of the pointers according to is_valid.
            if (is_valid_bin_search)
            {
                // If it is valid, move the outer pointer to the middle angle.
                outer_ptr_theta = middle_theta;
            }
            else
            {
                // If it is not valid, move the inner pointer to the middle angle.
                inner_ptr_theta = middle_theta;
            }
        }

        // Store booleans for the nan of the 2 solutions.
        bool ccw_nan = float.IsNaN(last_solution_ccw.x) || float.IsNaN(last_solution_ccw.y);
        bool cw_nan = float.IsNaN(last_solution_cw.x) || float.IsNaN(last_solution_cw.y);
        // Handle both solutions are nan case.
        if (ccw_nan && cw_nan)
        {
            Debug.LogWarning("Both solutions are invalid. Returning NaN point.");
            return new Vector2(float.NaN, float.NaN);
        }
        // Handle one solution is nan case.
        // Handle both are valid case.
        Vector3 solution;
        if (ccw_nan ^ cw_nan)
            solution = ccw_nan ? last_solution_cw : last_solution_ccw;
        else
        {
            // Calculate the distance of the two solutions to the target position.
            Vector3 last_solution_ccw_pos = _control_point_obj.CalcBsurface(last_solution_ccw.x, last_solution_ccw.y);
            Vector3 last_solution_cw_pos = _control_point_obj.CalcBsurface(last_solution_cw.x, last_solution_cw.y);

            // If one a solution is further away from the target position than the current point is, set it to the current point.
            if (Vector3.Distance(last_solution_ccw_pos, target_pos) > Vector3.Distance(curr_pos, target_pos))
                last_solution_ccw = curr_uv;
            if (Vector3.Distance(last_solution_cw_pos, target_pos) > Vector3.Distance(curr_pos, target_pos))
                last_solution_cw = curr_uv;

            // Find which one is closer.
            bool ccw_is_closer = Vector3.Distance(last_solution_ccw_pos, target_pos) < Vector3.Distance(last_solution_cw_pos, target_pos);

            // Set solution as the closer one.
            solution = ccw_is_closer ? last_solution_ccw : last_solution_cw;
        }
        // Draw a line from the current point to the solution point.
        Debug.DrawLine(curr_world, _control_point_obj.transform.TransformPoint(_control_point_obj.CalcBsurface(solution.x, solution.y)), Color.magenta);
        // Return the solution.
        return solution;
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
}
