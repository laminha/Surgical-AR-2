using UnityEngine;

public class ConformalToolpathingManager : MonoBehaviour
{
    public OVRCameraRig _tracking_space;
    LineRenderer _line_renderer;
    public BSurfaceGcodeGenerator _gcode_generator;
    public BsplineManager _control_point_obj;
    public LineRenderer _normal_axis_visual;
    void Start()
    {
        _line_renderer = GetComponent<LineRenderer>();
    }

    void Update()
    {
        // Follow the right controller.
        transform.position = _tracking_space.rightControllerAnchor.TransformPoint(_tracking_space.rightControllerAnchor.localPosition + Vector3.forward * 0.1f);

        // Update the linerender to the uv toolpath if the uvcount != linecount-1.
        // ie. if the number of uv points is not equal to the number of line renderer points, minus the "preview" point.
        // Also update if uv count is less than 5, to hard code edge cases.
        if (_gcode_generator._uv_points.Count != _line_renderer.positionCount - 1 || _gcode_generator._uv_points.Count < 5)
        {
            // Update the toolpath line.
            _line_renderer.positionCount = _gcode_generator._uv_points.Count;
            for (int i = 0; i < _gcode_generator._uv_points.Count; i++)
            {
                Vector3 point_local = _control_point_obj.CalcBsurface(_gcode_generator._uv_points[i].x, _gcode_generator._uv_points[i].y);
                Vector3 point_world = _control_point_obj.transform.TransformPoint(point_local);
                _line_renderer.SetPosition(i, point_world);
            }

            // Get current normal vector.
            if (_gcode_generator._uv_points.Count > 0)
            {
                Vector2 curr_uv = _gcode_generator._uv_points[_gcode_generator._uv_points.Count - 1];
                Vector3 curr_pos = _control_point_obj.CalcBsurface(curr_uv.x, curr_uv.y);
                Vector3 curr_normal_vec = Vector3.Cross(
                    _control_point_obj.CalcBSurfaceVelocityU(curr_uv.x, curr_uv.y),
                    _control_point_obj.CalcBSurfaceVelocityV(curr_uv.x, curr_uv.y)
                ).normalized;
                Vector3 curr_normal_axis_1 = curr_pos + curr_normal_vec;
                Vector3 curr_normal_axis_2 = curr_pos - curr_normal_vec;
                // Update the normal axis visual.
                _normal_axis_visual.positionCount = 2;
                _normal_axis_visual.SetPosition(0, _control_point_obj.transform.TransformPoint(curr_normal_axis_1));
                _normal_axis_visual.SetPosition(1, _control_point_obj.transform.TransformPoint(curr_normal_axis_2));
            }
        }

        // Draw the next segment of the toolpath using FindStepoverPoint.
        Vector2 next_uv = _gcode_generator.FindStepoverPoint(transform.position, sticky_mode: true, solution_type: BSurfaceGcodeGenerator.SolutionType.CWSolution);
        // If the next point is NaN, set the preview to the previous point and mark the point as unaddable.
        bool unaddable = false;
        if (float.IsNaN(next_uv.x) || float.IsNaN(next_uv.y))
        {
            next_uv = _gcode_generator._uv_points[^1];
            unaddable = true;
        }
        // Add it to the line renderer.
        Vector3 next_point_local = _control_point_obj.CalcBsurface(next_uv.x, next_uv.y);
        Vector3 next_point_world = _control_point_obj.transform.TransformPoint(next_point_local);
        _line_renderer.positionCount = _gcode_generator._uv_points.Count + 1; // Set the position count to include the new point.
        _line_renderer.SetPosition(_line_renderer.positionCount - 1, next_point_world);

        // If the A button is pressed, add the next point to the uv points.
        bool add_point_button_pressed = OVRInput.Get(OVRInput.Button.One, OVRInput.Controller.RTouch);
        // Only add at a rate.
        if (add_point_button_pressed && unaddable == false)
            if (Time.frameCount % 2 == 0)
            {
                _gcode_generator.AddPointsToTargetUvExclusive(next_uv.x, next_uv.y);
                _gcode_generator._uv_points.Add(next_uv);
            }
        // If the B button is pressed, delete the last _uv_point.
        // Only delete at a rate.
        bool delete_point_button_pressed = OVRInput.Get(OVRInput.Button.Two, OVRInput.Controller.RTouch);
        if (delete_point_button_pressed && _gcode_generator._uv_points.Count > 0)
            if (Time.frameCount % 1 == 0)
                _gcode_generator._uv_points.RemoveAt(_gcode_generator._uv_points.Count - 1);
    }
}
