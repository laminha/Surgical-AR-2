using Unity.VisualScripting;
using UnityEngine;

public class DrawingHandler : MonoBehaviour
{
    public LineRenderer _line_renderer;
    public float distance_between_points;
    void Awake()
    {
        _line_renderer = GetComponent<LineRenderer>();
    }
    void Update()
    {
        AddPointTask();
    }
    [ContextMenu("CreateTestLoop")]
    void CreateTestLoop()
    {
        int segments = 100;
        float r = 0.25f; // Radius.
        Vector3 center = new Vector3(0, 1, 0);

        _line_renderer.positionCount = segments + 1;
        for (int i = 0; i <= segments; i++)
        {
            float angle = 2 * Mathf.PI * i / segments;
            float x = center.x + r * Mathf.Cos(angle);
            float y = center.y + 0.03f * Mathf.Sin(3*angle) + 0.06f * Mathf.Cos(5*angle);
            float z = center.z + r * Mathf.Sin(angle);
            _line_renderer.SetPosition(i, new Vector3(x, y, z));
        }
    }
    [ContextMenu("ClearLineRenderer")]
    void ClearLineRenderer()
    {
        _line_renderer.positionCount = 0;
    }

    // "Task" meaning it should be run consistently.
    void AddPointTask()
    {
        // Exit if drawing button isn't pressed.
        if (OVRInput.Get(OVRInput.Button.One, OVRInput.Controller.RTouch) == false)
            return;

        // If this is the first point, add it and leave.
        if (_line_renderer.positionCount == 0)
        {
            _line_renderer.positionCount++;
            _line_renderer.SetPosition(0, transform.position);
            return;
        }
        
        // Leave if we aren't far enough from the last point.
        Vector3 prev_point = _line_renderer.GetPosition(_line_renderer.positionCount - 1);
        Vector3 curr_point = transform.position;
        float distance = Vector3.Distance(curr_point, prev_point);
        if (distance < distance_between_points)
            return;

        // Place a new point 1 {distance} closer.
        _line_renderer.positionCount++;
        float lerp_val = distance_between_points / distance;
        Vector3 new_point = Vector3.Lerp(prev_point, curr_point, lerp_val);
        _line_renderer.SetPosition(_line_renderer.positionCount - 1, new_point);
    }
}
