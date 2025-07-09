using UnityEngine;

public class ConformalToolpathingManager : MonoBehaviour
{
    public OVRCameraRig _tracking_space;
    LineRenderer _line_renderer;
    public BSurfaceGcodeGenerator _gcode_generator;
    public BsplineManager _control_point_obj;
    void Start()
    {
        _line_renderer = GetComponent<LineRenderer>();
    }

    void Update()
    {
        // Follow the right controller.
        transform.position = _tracking_space.rightControllerAnchor.TransformPoint(_tracking_space.rightControllerAnchor.localPosition + Vector3.forward * 0.1f);

        // Update the linerender to the uv toolpath if the uvcount > linecount-1.
        // ie. if the number of uv points is greater than the number of line renderer points, minus the "preview" point.
        // Also update if uv count is 1, to hard code an edge case.
        if (_gcode_generator._uv_points.Count > _line_renderer.positionCount - 1 || _gcode_generator._uv_points.Count == 1)
        {
            _line_renderer.positionCount = _gcode_generator._uv_points.Count;
            for (int i = 0; i < _gcode_generator._uv_points.Count; i++)
            {
                Vector3 point_local = _control_point_obj.CalcBsurface(_gcode_generator._uv_points[i].x, _gcode_generator._uv_points[i].y);
                Vector3 point_world = _control_point_obj.transform.TransformPoint(point_local);
                _line_renderer.SetPosition(i, point_world);
            }
        }

        // Draw the next segment of the toolpath using FindStepoverPoint.
        Vector2 next_uv = _gcode_generator.FindStepoverPoint(transform.position);

        // If the next point is NaN, do not add it.
        if (float.IsNaN(next_uv.x) || float.IsNaN(next_uv.y))
        {
            return;
        }

        // Add it to the line renderer.
        Vector3 next_point_local = _control_point_obj.CalcBsurface(next_uv.x, next_uv.y);
        Vector3 next_point_world = _control_point_obj.transform.TransformPoint(next_point_local);
        _line_renderer.positionCount = _gcode_generator._uv_points.Count + 1; // Set the position count to include the new point.
        _line_renderer.SetPosition(_line_renderer.positionCount - 1, next_point_world);

        // If the A button is pressed, add the next point to the uv points.
        bool add_point_button_pressed = OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch);
        if (add_point_button_pressed)
        {
            _gcode_generator.AddPointsToTargetUvExclusive(next_uv.x, next_uv.y);
            _gcode_generator._uv_points.Add(next_uv);
        }
    }
}
