#pragma warning disable IDE0090
#pragma warning disable IDE0079
#pragma warning disable IDE0060
#pragma warning disable IDE0044

using System.Security.Cryptography;
using UnityEngine;
using UnityEngine.Assertions;
using UnityEngine.UIElements;
[RequireComponent(typeof(LineRenderer))]
public class Bezier : MonoBehaviour
{
    public Vector3[,] _control_points; 
    LineRenderer _line_renderer_template;
    int _curve_count = 0; // Along one spline.
    int _spline_count = 0; // In one direction.
    const int kSegmentCount = 50;
    public int _debug_size = 2; // Number of splines (in a direction) for create control points for.

    [ContextMenu("DebugSetControlpoints")]
    void DebugSetControlpoints()
    {
        _control_points = new Vector3[3*_debug_size+1, 3*_debug_size+1];
        float divisor = 3 * _debug_size;
        for (int i = 0; i < 3 * _debug_size + 1; i++)
        {
            for (int j = 0; j < 3 * _debug_size + 1; j++)
            {
                _control_points[i, j] = new Vector3(0.2f * i / divisor - 0.1f, Random.Range(-0.1f, -0.05f), 0.2f * j / divisor - 0.1f);
            }
        }
    }

    // Edit bezier handles (like photoshop spline handles) to make surface tangent everywhere
    void TangentizeControlpoints()
    {
        for (int i = 0; i < _control_points.GetLength(0); i++)
        {
            for (int j = 0; j < _control_points.GetLength(0); j++)
            {
                // Do nothing if point is a clamped point (line touches the control point)
                if (i % 3 == 0 || j % 3 == 0)
                    continue;
            }
        }
    }

    [ContextMenu("Start")]
    void Start()
    {
        DebugSetControlpoints();

        // Set _line_renderer_template.
        _line_renderer_template = GetComponent<LineRenderer>();

        // Assert control point grid is an nxn array.
        Assert.IsTrue(_control_points.GetLength(0) == _control_points.GetLength(1));
        _curve_count = (int)(_control_points.GetLength(0) - 1) / 3;
        _spline_count = _curve_count + 1;

        DebugSetControlpoints();
        DrawGrid();
    }

    [ContextMenu("DrawCurve")]
    void DrawGrid()
    {
        // Kill all children greater than needed.
        while (transform.childCount > _spline_count)
            GameObject.DestroyImmediate(transform.GetChild(0).gameObject);

        // Draw splines
        for (int draw_direction = 0; draw_direction < 2; draw_direction++)
            for (int curr_spline = 0; curr_spline < _spline_count; curr_spline++)
            {
                // Try to find instance of a line renderer.
                int spline_id = curr_spline + draw_direction * _spline_count;
                GameObject curr_line_renderer_obj;
                LineRenderer curr_line_renderer;
                try {
                    curr_line_renderer_obj = transform.GetChild(spline_id).gameObject;
                    curr_line_renderer = curr_line_renderer_obj.GetComponent<LineRenderer>();
                }
                catch {
                    // Create new instance of line renderer.
                    curr_line_renderer_obj = new();
                    curr_line_renderer_obj.transform.SetParent(transform, false); // False = world position doesnt stay.
                    curr_line_renderer_obj.name = "SplineObject";
                    curr_line_renderer = curr_line_renderer_obj.AddComponent<LineRenderer>();

                    // Make is the same as the template (better way to do this?)
                    curr_line_renderer.widthMultiplier = _line_renderer_template.widthMultiplier;
                    curr_line_renderer.colorGradient = _line_renderer_template.colorGradient;
                    curr_line_renderer.material = _line_renderer_template.sharedMaterial;
                    curr_line_renderer.useWorldSpace = _line_renderer_template.useWorldSpace;
                }

                // Draw spline.
                for (int curr_curve = 0; curr_curve < _curve_count; curr_curve++)
                    for (int curr_segment = 0; curr_segment < kSegmentCount+1; curr_segment++)
                    {
                        float t = curr_segment / (float)kSegmentCount;
                        int nodeIndex = curr_curve * 3;
                        int spline_index = curr_spline * 3;
                        Vector3 pixel;
                        if (draw_direction == 0)
                            pixel = CalculateCubicBezierPoint(t,
                                _control_points[spline_index, nodeIndex],
                                _control_points[spline_index, nodeIndex + 1],
                                _control_points[spline_index, nodeIndex + 2],
                                _control_points[spline_index, nodeIndex + 3]);
                        else
                            pixel = CalculateCubicBezierPoint(t,
                                _control_points[nodeIndex, spline_index],
                                _control_points[nodeIndex + 1, spline_index],
                                _control_points[nodeIndex + 2, spline_index],
                                _control_points[nodeIndex + 3, spline_index]);

                        curr_line_renderer.positionCount = kSegmentCount * curr_curve + curr_segment + 1;
                        curr_line_renderer.SetPosition(curr_line_renderer.positionCount - 1, pixel);
                    }
            }
    }
    Vector3 CalculateCubicBezierPoint(float t, Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3)
    {
        float u = 1 - t;
        float tt = t * t;
        float uu = u * u;
        float uuu = uu * u;
        float ttt = tt * t;

        Vector3 p = uuu * p0;
        p += 3 * uu * t * p1;
        p += 3 * u * tt * p2;
        p += ttt * p3;

        return p;
    }
    Vector3 CalculateCubicBezierPointOnSurface(float u, float v, Vector3[,] cp)
    {
        return new Vector3();
    }
}
