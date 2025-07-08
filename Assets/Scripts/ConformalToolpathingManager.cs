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

        // Set the linerender to the uv toolpath.
        _line_renderer.positionCount = _gcode_generator._uv_points.Count;
        for (int i = 0; i < _gcode_generator._uv_points.Count; i++)
        {
            Vector3 point_local = _control_point_obj.CalcBsurface(_gcode_generator._uv_points[i].x, _gcode_generator._uv_points[i].y);
            Vector3 point_world = _control_point_obj.transform.TransformPoint(point_local);
            _line_renderer.SetPosition(i, point_world);
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
        _line_renderer.positionCount++;
        _line_renderer.SetPosition(_line_renderer.positionCount - 1, next_point_world);

        // If the A button is pressed, add the next point to the uv points.
        if (OVRInput.Get(OVRInput.Button.One, OVRInput.Controller.RTouch))
        {
            _gcode_generator.AddPointsToTargetUvExclusive(next_uv.x, next_uv.y);
        }
    }
}
