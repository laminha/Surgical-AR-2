#pragma warning disable IDE0056 // Use index operator
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using Unity.Mathematics;
 
public class BSurfaceGcodeGenerator : MonoBehaviour
{
    public BsplineManager _control_point_obj;
    readonly List<Vector2> _uv_points = new();
    public float _gcode_step_size = 0.1f; // The maximum distance between two points in the gcode (unity units).
    public float _feedrate = 10f; // Feedrate in unity mm/s.
    private string _file_name;
    private string _file_path;
    void Start()
    {
        _file_name = "BSurface.gcode";
        _file_path = Path.Combine(Application.persistentDataPath, _file_name);
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
        _uv_points.Add(new Vector2(1, 1));
        // Circle perimeter of the BSurface.
        AddPointsToTargetUv(_control_point_obj._size - 2, 1);
        AddPointsToTargetUv(_control_point_obj._size - 2, _control_point_obj._size - 2);
        AddPointsToTargetUv(1, _control_point_obj._size - 2);
        AddPointsToTargetUv(1, 1);

        // Draw the gcode path in the scene view for debugging.
        // alternate color from white to black for each segment.
        for (int i = 0; i < _uv_points.Count - 1; i++)
        {
            Vector3 start = transform.TransformPoint(_control_point_obj.CalcBsurface(_uv_points[i].x, _uv_points[i].y));
            Vector3 end = transform.TransformPoint(_control_point_obj.CalcBsurface(_uv_points[i + 1].x, _uv_points[i + 1].y));
            if (i % 2 == 0)
                Debug.DrawLine(start, end, Color.white, 10f);
            else
                Debug.DrawLine(start, end, Color.black, 10f);
            // Debug.Log("uv point: " + _uv_points[i] + " -> " + _uv_points[i + 1]);
        }

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
    void AddPointsToTargetUv(float u, float v)
    {
        int counter = 0;
        while (true)
        {
            // Exit loop if 1000 loops have been reached.
            if (counter++ > 1000)
            {
                Debug.LogError("Infinite loop detected in AddGcodesToTargetUv. Exiting.");
                break;
            }

            // Calculate current uv position.
            Vector2 current_position = _uv_points[_uv_points.Count - 1];
            // Calculate final position.
            Vector2 final_position = new(u, v);
            // Calculate unit direction vector.
            Vector2 direction = (final_position - current_position).normalized;
            // If the direction is zero, we are already at the final position.
            if (direction == Vector2.zero)
            {
                // Add the final position to the list and exit the loop.
                _uv_points.Add(final_position);
                break;
            }
            // Calculate how the uv direction changes x,y,&z.
                Vector3 direction_3d =
                direction.x * _control_point_obj.CalcBSurfaceVelocityU(u, v) +
                direction.y * _control_point_obj.CalcBSurfaceVelocityV(u, v);
            // Divide the direction by the magnitude of the 3D direction vector.
            Vector2 scaled_direction = direction / direction_3d.magnitude;
            // Calculate the actual step size in uv space.
            Vector2 uv_step = scaled_direction * _gcode_step_size;
            // Check if the distance to the final position is smaller than the step size.
            if (Vector2.Distance(current_position, final_position) < uv_step.magnitude)
            {
                // If so, add the final position to the list and exit the loop.
                _uv_points.Add(final_position);
                break;
            }
            // Otherwise, add the current position to the list and update the uv position.
            _uv_points.Add(current_position + uv_step);
        }
    }
}
