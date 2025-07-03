using System;
using System.Collections.Generic;
using UnityEngine;

public class BsurfaceGridlineManager : MonoBehaviour
{
    public BsplineManager _bspline;
    public LineRenderer _line_renderer;
    public float _resolution_step = 0.5f; // Amount that u or v steps between line points.
    void Start()
    {
        _line_renderer.GetComponent<LineRenderer>();
    }
    [ContextMenu("Update")]
    void Update()
    {
        if (_bspline._control_points == null)
            return;
        int num_points = _bspline._control_points.GetLength(0);

        // Zigzag starting from uv = (1,1), drawing horizontal lines (ie. u direction).
        List<Vector3> line_points_list = new();
        for (float v = 1; Math.Round(v, 5) <= num_points - 2; v += _resolution_step)
        {
            for (float u = 1; Math.Round(u, 2) <= num_points - 2; u += _resolution_step)
                line_points_list.Add(_bspline.CalcBsurface(u, v));
            v += _resolution_step;
            if (Math.Round(v, 5) > num_points - 2)
                break;
            for (float u = num_points - 2; Math.Round(u, 2) >= 1; u -= _resolution_step)
                line_points_list.Add(_bspline.CalcBsurface(u, v));
        }
        // If we ended at the top-right corner, navigate to the top-left.
        if (num_points % 2 == 0)
        {
            float v = num_points - 2;
            for (float u = num_points - 2; Math.Round(u, 2) >= 1; u -= _resolution_step)
                line_points_list.Add(_bspline.CalcBsurface(u, v));
        }
        // Zigzag starting from uv = (1, num_points-2), drawing vertical lines.
        for (float u = 1; Math.Round(u, 5) <= num_points - 2; u += _resolution_step)
        {
            for (float v = num_points - 2; Math.Round(v, 2) >= 1; v -= _resolution_step)
                line_points_list.Add(_bspline.CalcBsurface(u, v));
            u += _resolution_step;
            if (Math.Round(u, 5) > num_points - 2)
                break;
            for (float v = 1; Math.Round(v, 2) <= num_points - 2; v += _resolution_step)
                line_points_list.Add(_bspline.CalcBsurface(u, v));
        }

        // Add list points to line renderer.
        _line_renderer.positionCount = line_points_list.Count;
        for (int i = 0; i < line_points_list.Count; i++)
            _line_renderer.SetPosition(i, line_points_list[i]);
    }
}
