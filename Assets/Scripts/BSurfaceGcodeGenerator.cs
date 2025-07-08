#pragma warning disable CS0162 // Unreachable code detected
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using Unity.VisualScripting;

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
    public int angular_resolution_per_rev = 100;
    public Vector2 FindStepoverPoint(Vector3 target_pos)
    {
        // WORRY ABOUT RUNTIME AFTER IT WORKS.

        // If there are no uv points, add the first point.
        if (_uv_points.Count == 0)
        {
            _uv_points.Add(new Vector2(0f, 0.5f));
        }

        // Define variables.
        Vector2 curr_uv = _uv_points[_uv_points.Count - 1];
        Vector3 curr_pos = _control_point_obj.CalcBsurface(curr_uv.x, curr_uv.y);
        Vector3 curr_world = _control_point_obj.transform.TransformPoint(curr_pos);

        // Find the uv direction that moves the closest to the target position.
        Vector2 uv_dir_closest = new();
        float min_angle = float.MaxValue;
        for (int i = 0; i < angular_resolution_per_rev; i++)
        {
            float theta = 2*Mathf.PI * i / angular_resolution_per_rev;
            Vector3 velo_u = Mathf.Cos(theta) * _control_point_obj.CalcBSurfaceVelocityU(curr_uv.x, curr_uv.y);
            Vector3 velo_v = Mathf.Sin(theta) * _control_point_obj.CalcBSurfaceVelocityV(curr_uv.x, curr_uv.y);
            Vector3 velocity = velo_u + velo_v;
            Vector3 curr_to_target = target_pos - _control_point_obj.transform.TransformPoint(curr_pos);
            float angle_velo_target = Vector3.Angle(velocity, curr_to_target);
            if (angle_velo_target < min_angle)
            {
                min_angle = angle_velo_target;
                uv_dir_closest = new Vector2(Mathf.Cos(theta), Mathf.Sin(theta)).normalized;
            }
        }

        // Normalize the uv direction vector in 3D space.
        Vector3 uv_dir_3d = uv_dir_closest.x * _control_point_obj.CalcBSurfaceVelocityU(curr_uv.x, curr_uv.y) +
                            uv_dir_closest.y * _control_point_obj.CalcBSurfaceVelocityV(curr_uv.x, curr_uv.y);
        uv_dir_closest /= uv_dir_3d.magnitude;
        // Scale the uv direction vector by the step size.
        uv_dir_closest *= _calculation_step_size;
        // Scale the 3D direction vector by the step size.
        uv_dir_3d = _calculation_step_size * uv_dir_3d.normalized;

        // Define variables.
        Vector2 next_uv = curr_uv + uv_dir_closest;
        Vector3 next_pos = curr_pos + uv_dir_3d;
        Vector3 next_world = _control_point_obj.transform.TransformPoint(next_pos);

        // Check if next_uv is inside the circle inscribing the BSurface.
        bool is_valid = Vector2.Distance(new(0.5f,0.5f), next_uv) <= 0.5f;
        // Check if it is too close to any other point in _uv_points.
        for (int i = 0; i < _uv_points.Count; i++)
        {
            if (is_valid == false)
                break;

            Vector2 other_uv = _uv_points[i];
            // Calculate the 3D position of the other uv point.
            Vector3 other_pos = _control_point_obj.CalcBsurface(other_uv.x, other_uv.y);
            // Calculate the distance between the next point and the other point.
            float distance = Vector3.Distance(next_pos, other_pos);
            // If the distance is less than the stepover distance, the point is not valid.
            if (distance < _stepover)
                is_valid = false;
        }

        // Draw a line from the current point to the target position (in world space).
        Debug.DrawLine(curr_world, target_pos, Color.green);

        // If it isn't too close, return the uv point.
        if (is_valid == true)
        {
            // Draw a line from the current point to the next point in the "closest" direction.
            Debug.DrawLine(curr_world, next_world, Color.magenta);
            // If the next point is further away from the target position than the current point, return the current point.
            if (Vector3.Distance(next_world, target_pos) > Vector3.Distance(curr_world, target_pos))
                return curr_uv;
            else
                return next_uv;
        }

        for (int i = 0; i <= angular_resolution_per_rev / 2; i++)
        {
            if (i == 0)
                continue;

            // Perform CCW and CW shifts every loop, starting with CCW.
            float theta_shift = 2 * Mathf.PI * i / angular_resolution_per_rev;
            // Calculate the new uv direction vector as old next_uv rotated by theta_shift.
            Vector2 uv_dir_shifted = new(
                Mathf.Cos(theta_shift) * uv_dir_closest.x - Mathf.Sin(theta_shift) * uv_dir_closest.y,
                Mathf.Sin(theta_shift) * uv_dir_closest.x + Mathf.Cos(theta_shift) * uv_dir_closest.y
            );
            // Renormalize in 3D space.
            uv_dir_3d = uv_dir_shifted.x * _control_point_obj.CalcBSurfaceVelocityU(curr_uv.x, curr_uv.y) +
                        uv_dir_shifted.y * _control_point_obj.CalcBSurfaceVelocityV(curr_uv.x, curr_uv.y);
            uv_dir_shifted /= uv_dir_3d.magnitude;
            // Rescale the uv direction vector by the calculation step size.
            uv_dir_shifted *= _calculation_step_size;
            // Do the same for the 3D vector.
            uv_dir_3d = _calculation_step_size * uv_dir_3d.normalized;

            // Check if it is valid.
            next_uv = curr_uv + uv_dir_shifted;
            next_pos = curr_pos + uv_dir_3d;
            next_world = _control_point_obj.transform.TransformPoint(next_pos);
            // Check if next_uv is inside the circle inscribing the BSurface.
            is_valid = Vector2.Distance(new(0.5f, 0.5f), next_uv) <= 0.5f;
            // Check if it is too close to any other point in _uv_points.
            for (int i_uv = 0; i_uv < _uv_points.Count; i_uv++)
            {
                if (is_valid == false)
                    break;

                Vector2 other_uv = _uv_points[i_uv];
                // Calculate the 3D position of the other uv point.
                Vector3 other_pos = _control_point_obj.CalcBsurface(other_uv.x, other_uv.y);
                // Calculate the distance between the next point and the other point.
                float distance = Vector3.Distance(next_pos, other_pos);
                // If the distance is less than the stepover distance, the point is not valid.
                if (distance < _stepover)
                    is_valid = false;
            }
            // Return uv vector if it is valid.
            if (is_valid == true)
            {
                // Draw a line from the current point to the next point in the "shifted" direction.
                Debug.DrawLine(curr_world, next_world, Color.magenta);
                // If the next point is further away from the target position than the current point, return the current point.
                if (Vector3.Distance(next_world, target_pos) > Vector3.Distance(curr_world, target_pos))
                    return curr_uv;
                else
                    return next_uv;
            }

            // Do the same, but for the CW shift.
            theta_shift = -theta_shift;
            // Calculate the new uv direction vector as old next_uv rotated by theta_shift.
            uv_dir_shifted = new(
                Mathf.Cos(theta_shift) * uv_dir_closest.x - Mathf.Sin(theta_shift) * uv_dir_closest.y,
                Mathf.Sin(theta_shift) * uv_dir_closest.x + Mathf.Cos(theta_shift) * uv_dir_closest.y
            );
            // Renormalize in 3D space.
            uv_dir_3d = uv_dir_shifted.x * _control_point_obj.CalcBSurfaceVelocityU(curr_uv.x, curr_uv.y) +
                        uv_dir_shifted.y * _control_point_obj.CalcBSurfaceVelocityV(curr_uv.x, curr_uv.y);
            uv_dir_shifted /= uv_dir_3d.magnitude;
            // Rescale the uv direction vector by the calculation step size.
            uv_dir_shifted *= _calculation_step_size;
            // Do the same for the 3D vector.
            uv_dir_3d = _calculation_step_size * uv_dir_3d.normalized;

            // Check if it is valid.
            next_uv = curr_uv + uv_dir_shifted;
            next_pos = curr_pos + uv_dir_3d;
            next_world = _control_point_obj.transform.TransformPoint(next_pos);
            // Check if next_uv is inside the circle inscribing the BSurface.
            is_valid = Vector2.Distance(new(0.5f, 0.5f), next_uv) <= 0.5f;
            // Check if it is too close to any other point in _uv_points.
            for (int i_uv2 = 0; i_uv2 < _uv_points.Count; i_uv2++)
            {
                if (is_valid == false)
                    break;

                Vector2 other_uv = _uv_points[i_uv2];
                // Calculate the 3D position of the other uv point.
                Vector3 other_pos = _control_point_obj.CalcBsurface(other_uv.x, other_uv.y);
                // Calculate the distance between the next point and the other point.
                float distance = Vector3.Distance(next_pos, other_pos);
                // If the distance is less than the stepover distance, the point is not valid.
                if (distance < _stepover)
                    is_valid = false;
            }
            // Return uv vector if it is valid.
            if (is_valid == true)
            {
                // Draw a line from the current point to the next point in the "shifted" direction.
                Debug.DrawLine(curr_world, next_world, Color.magenta);
                // If the next point is further away from the target position than the current point, return the current point.
                if (Vector3.Distance(next_world, target_pos) > Vector3.Distance(curr_world, target_pos))
                    return curr_uv;
                else
                    return next_uv;
            }
        }
        // If no valid point was found, return a NaN point.
        Debug.LogWarning("No valid point found in FindStepoverPoint. Returning NaN point.");
        return new Vector2(float.NaN, float.NaN);
    }
}
