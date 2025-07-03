using Newtonsoft.Json.Converters;
using UnityEngine;
using UnityEngine.EventSystems;

public class ControlPolygonLineManager : MonoBehaviour
{
    public BsplineManager _bspline;
    [ContextMenu("Start")]
    void Start()
    {
        // Ensure we have a BsplineManager component attached.
        if (_bspline == null)
            _bspline = GetComponent<BsplineManager>();
    }
    void OnDrawGizmos()
    {
        Update();
    }
    [ContextMenu("Update")]
    void Update()
    {
        bool right_grip = OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.RTouch);
        bool left_grip = OVRInput.Get(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.LTouch);

        LineRenderer line_renderer = GetComponent<LineRenderer>();
        if (right_grip || left_grip || !Application.isPlaying)
            line_renderer.enabled = true;
        else
            line_renderer.enabled = false;

        // Return if control points aren't defined yet.
        if (_bspline._control_points == null)
            return;

        // Update the line renderer positions
            // Starting from control point (0,0), zigzag to draw horizontal lines, then zigzag back to draw vertical lines.
            int num_points = _bspline._control_points.GetLength(0);
        line_renderer.positionCount = num_points * num_points * 2; // Total number of points twice over.
        int line_renderer_index = 0;

        // Draw horizontal lines first.
        for (int j = 0; j < num_points; j++)
            if (j % 2 == 0)
                // Draw horizontal line left to right.
                for (int i = 0; i < num_points; i++)
                {
                    line_renderer.SetPosition(line_renderer_index, _bspline._control_points[i, j]);
                    line_renderer_index++;
                }
            else
                // Draw next row, right to left.
                for (int i = num_points - 1; i >= 0; i--)
                {
                    line_renderer.SetPosition(line_renderer_index, _bspline._control_points[i, j]);
                    line_renderer_index++;
                }

        // If num_points is even, we are at the upper-left corner, else, the upper-right.
        bool is_even = num_points % 2 == 0;
        int for_start = is_even ? (0) : (num_points - 1);
        int for_increment = is_even ? 1 : -1;
        // Draw vertical lines next.
        for (int i = for_start; is_even ? (i < num_points) : (i >= 0); i += for_increment)
            if (i % 2 == 1)
                for (int j = 0; j < num_points; j++)
                {
                    // Draw vertical line top to bottom.
                    line_renderer.SetPosition(line_renderer_index, _bspline._control_points[i, j]);
                    line_renderer_index++;
                }
            else
                for (int j = num_points - 1; j >= 0; j--)
                {
                    // Draw next column, bottom to top.
                    line_renderer.SetPosition(line_renderer_index, _bspline._control_points[i, j]);
                    line_renderer_index++;
                }
    }
}