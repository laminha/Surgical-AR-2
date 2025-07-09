using System;
using OVR.OpenVR;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

public class BsplineManager : MonoBehaviour
{
    public Vector3[,] _control_points;
    public int _size;
    public float[] _knot_vector;
    public bool _generate_gizmos = true; // Toggle to disable gizmos in the scene view.
    const int _surface_degree = 3;

    // This function is awake, not start because the surface needs to be defined before it
    // is used in the others' Start().
    void Awake()
    {
        Debug.Log("Awake called in BsplineManager. Defining surface.");
        DefineSurface();
    }
    [ContextMenu("DefineSurface")]
    void DefineSurface()
    {
        _size = _knot_vector.Length - _surface_degree - 1;
        Debug.Log("Defining surface with size: " + _size + "x" + _size);
        // Default control points will be placed in a 0.2x0.2 grid, with random heights starting at -0.1.
        _control_points = new Vector3[_size, _size];
        for (int u = 0; u < _control_points.GetLength(0); u++)
            for (int v = 0; v < _control_points.GetLength(0); v++)
                _control_points[u, v] = new Vector3(
                    -0.1f + 0.2f * u / (_control_points.GetLength(0) - 1),
                    -0.075f + UnityEngine.Random.Range(-0.025f, 0.025f),
                    -0.1f + 0.2f * v / (_control_points.GetLength(0) - 1)
                );
    }
    void OnDrawGizmos()
    { 
        // Check if surface is defined.
        if (_control_points == null)
        {
            DefineSurface();
        }
        
        // If gizmos disabled, exit.
        if (_generate_gizmos == false)
            return;

        // Draw surface uv = [0, 1].
        Gizmos.color = Color.violet;
        const float resolution = 5;
        for (int ui = 0; ui < resolution; ui++)
            for (int vi = 0; vi <= resolution; vi++)
            {
                float u = Mathf.InverseLerp(0, resolution, ui);
                float v = Mathf.InverseLerp(0, resolution, vi);
                float u_next = Mathf.InverseLerp(0, resolution, ui + 1);
                // Horizontal surface gridlines.
                Gizmos.DrawLine(
                    transform.TransformPoint(CalcBsurface(u, v)),
                    transform.TransformPoint(CalcBsurface(u_next, v))
                );
                // Note, for this second function, we do a hack, switching u and v, to get the vertical surface gridlines.
                Gizmos.DrawLine(
                    transform.TransformPoint(CalcBsurface(v, u)),
                    transform.TransformPoint(CalcBsurface(v, u_next))
                );
            }

        // Draw control points/polygons.
        Gizmos.color = Color.blue;
        for (int i = 0; i < _size; i++)
            for (int j = 0; j < _size; j++)
            {
                Gizmos.DrawSphere(transform.TransformPoint(_control_points[i, j]), 0.001f*transform.localScale.x);
                // Use try-catch to skip bad indices (easier than if statements).
                try
                {
                    Gizmos.DrawLine(
                        transform.TransformPoint(_control_points[i, j]),
                        transform.TransformPoint(_control_points[i, j + 1])
                    );
                } catch { }
                
                try
                {
                    Gizmos.DrawLine(
                        transform.TransformPoint(_control_points[i, j]),
                        transform.TransformPoint(_control_points[i + 1, j])
                    );
                } catch { }
            }

        // Draw du, dv, and normal vector at integer+editor_time u&v.
        for (int ui = -1; ui <= resolution; ui++)
            for (int vi = -1; vi <= resolution; vi++)
            {
                float editor_time_mod2 = (float)EditorApplication.timeSinceStartup % 2;
                float editor_time_mod2_1 = editor_time_mod2 * ((editor_time_mod2 < 1) ? 1f : 0f);
                float editor_time_mod2_2 = (editor_time_mod2 - 1) * ((editor_time_mod2 >= 1) ? 1f : 0f);
                float u_moving = Mathf.InverseLerp(0, resolution, ui + editor_time_mod2_1);
                float v_moving = Mathf.InverseLerp(0, resolution, vi + editor_time_mod2_2);;

                Vector3 surface_pos = CalcBsurface(u_moving, v_moving);
                Vector3 u_vec = CalcBSurfaceVelocityU(u_moving, v_moving).normalized;
                Vector3 v_vec = CalcBSurfaceVelocityV(u_moving, v_moving).normalized;
                Vector3 norm_vec = -Vector3.Cross(u_vec, v_vec).normalized; // Result is negated because unity uses LHR??
                Gizmos.color = Color.red;
                Gizmos.DrawLine(
                    transform.TransformPoint(surface_pos),
                    transform.TransformPoint(surface_pos + u_vec * 0.01f)
                );
                Gizmos.color = Color.green;
                Gizmos.DrawLine(
                    transform.TransformPoint(surface_pos),
                    transform.TransformPoint(surface_pos + v_vec * 0.01f)
                );
                Gizmos.color = Color.orange;
                Gizmos.DrawLine(
                    transform.TransformPoint(surface_pos),
                    transform.TransformPoint(surface_pos + norm_vec * 0.01f)
                );
            }
    }
    public float CoxDeBoorAlgorithmRecursive(int target_knot, int degree, float t)
    {
        // Define variables (const)
        int k = target_knot;
        int d = degree;
        float[] tk = _knot_vector;

        // Send error if we know t is outside the partitions of unity.
        if (t < tk[d] || tk[_knot_vector.Length - d - 1] < t)
            Debug.LogError($"t is outside of partitions of unity. t = {t}");

        // Return base case.
        if (d == 0)
        {
            if (k == _knot_vector.Length - 2) // Index of second last knot.
                return (t >= tk[k] && t <= tk[k + 1]) ? 1 : 0;
            else
                return (t >= tk[k] && t < tk[k + 1]) ? 1 : 0;
        }

        // Return recursive case.
        float upward_slope_term = 1;
        float downward_slope_term = 1;
        if (tk[k + d] - tk[k] != 0)
            upward_slope_term = (t - tk[k]) / (tk[k + d] - tk[k]);
        if (tk[k + d + 1] - tk[k + 1] != 0)
            downward_slope_term = (tk[k + d + 1] - t) / (tk[k + d + 1] - tk[k + 1]);
        return (
            upward_slope_term * CoxDeBoorAlgorithmRecursive(k, d - 1, t) +
            downward_slope_term * CoxDeBoorAlgorithmRecursive(k + 1, d - 1, t)
        );
    }
    public float CoxDeBoorAlgorithmDerivative(int target_knot, float t)
    {
        // Just use difference quotient, should be fine.
        const float h = 0.001f;
        // Get the basis value at t.
        float curr_val = CoxDeBoorAlgorithmRecursive(target_knot, _surface_degree, t);
        // Check if a step to the left will leave the partitions of unity.
        // Will work as long as the partitions of unity are wider than 2*h (basically always works).
        if (t - h < _knot_vector[_surface_degree] == false)
        {
            float left_diff = CoxDeBoorAlgorithmRecursive(target_knot, _surface_degree, t - h);
            return (curr_val - left_diff) / h;
        }
        else
        {
            float right_diff = CoxDeBoorAlgorithmRecursive(target_knot, _surface_degree, t + h);
            return (right_diff - curr_val) / h;
        }
    }
    public float BasisFunction3D(int control_point_index_u, int control_point_index_v, float u, float v)
    {
        return (
            CoxDeBoorAlgorithmRecursive(control_point_index_u, _surface_degree, u) *
            CoxDeBoorAlgorithmRecursive(control_point_index_v, _surface_degree, v)
        );
    }
    float VelocityBasisFunctionU3D(int control_point_index_u, int control_point_index_v, float u, float v)
    {
        return (
            CoxDeBoorAlgorithmDerivative(control_point_index_u, u) *
            CoxDeBoorAlgorithmRecursive(control_point_index_v, _surface_degree, v)
        );
    }
    float VelocityBasisFunctionV3D(int control_point_index_u, int control_point_index_v, float u, float v)
    {
        return (
            CoxDeBoorAlgorithmRecursive(control_point_index_u, _surface_degree, u) * 
            CoxDeBoorAlgorithmDerivative(control_point_index_v, v)
        );
    }
    public Vector3 CalcBsurface(float u, float v)
    {
        Vector3 output = new(0, 0, 0);
        for (int i = 0; i < _size; i++)
            for (int j = 0; j < _size; j++)
            {
                // Continue if u or v are outside the 5 knots that the basis function is > 0.
                if (u < _knot_vector[i] || u > _knot_vector[i + _surface_degree + 1] ||
                    v < _knot_vector[j] || v > _knot_vector[j + _surface_degree + 1])
                    continue;
                output += BasisFunction3D(i, j, u, v) * _control_points[i, j];
            }
        return output;
    }
    public Vector3 CalcBSurfaceVelocityU(float u, float v)
    {
        Vector3 output = new(0, 0, 0);
        for (int i = 0; i < _size; i++)
            for (int j = 0; j < _size; j++) {
                // Continue if u or v are outside the 5 knots that the basis function is > 0.
                if (u < _knot_vector[i] || u > _knot_vector[i + _surface_degree + 1] ||
                    v < _knot_vector[j] || v > _knot_vector[j + _surface_degree + 1])
                    continue;
                output += VelocityBasisFunctionU3D(i, j, u, v) * _control_points[i, j];
            }
        return output;
    }
    public Vector3 CalcBSurfaceVelocityV(float u, float v)
    {
        Vector3 output = new(0, 0, 0);
        for (int i = 0; i < _size; i++)
            for (int j = 0; j < _size; j++)
            {
                // Continue if u or v are outside the 5 knots that the basis function is > 0.
                if (u < _knot_vector[i] || u > _knot_vector[i + _surface_degree + 1] ||
                    v < _knot_vector[j] || v > _knot_vector[j + _surface_degree + 1])
                    continue;
                output += VelocityBasisFunctionV3D(i, j, u, v) * _control_points[i, j];
            }
        return output;
    }
}