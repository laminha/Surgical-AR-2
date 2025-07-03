using System;
using UnityEngine;
using MathNet.Numerics.LinearAlgebra;

public class SurfaceFittingManager : MonoBehaviour
{
    public float _lambda_regularization = 1; // Regularization parameter for the least squares fitting.
    int _major_diameter_index; // Index of the major diameter in the drawn loop.
    bool _loop_is_clockwise;
    public BsplineManager _bspline;
    public LineRenderer _drawn_loop;
    [ContextMenu("Fit Elliptical Initial Guess")]
    public void FitSurfaceToDrawingEllipticalHeuristic()
    {
        // Exit if there are less than 10 points in the drawn loop.
        if (_drawn_loop.positionCount < 10)
            return;

        // Fill the possible spots where the last and first point at too far apart by linearly interpolating.
        const float resolution = 0.01f; // This number should be same as drawing spacing resolution.
        int num_points = _drawn_loop.positionCount;
        float loop_open_distance = Vector3.Distance(_drawn_loop.GetPosition(0), _drawn_loop.GetPosition(num_points - 1));
        int num_new_points = (int)MathF.Floor(loop_open_distance / resolution);
        Vector3 last_point = _drawn_loop.GetPosition(num_points - 1);
        Vector3 first_point = _drawn_loop.GetPosition(0);
        _drawn_loop.positionCount += num_new_points;
        if (num_new_points != 1)
            for (int i = 0; i < num_new_points; i++)
                _drawn_loop.SetPosition(num_points + i, Vector3.Lerp(last_point, first_point, 1f * i / (num_new_points - 1)));
        else
            _drawn_loop.SetPosition(num_points, first_point);

        // Find the center of mass of the points
        num_points = _drawn_loop.positionCount;
        Vector3 center_of_mass = new();
        for (int i = 0; i < num_points; i++)
            center_of_mass += _drawn_loop.GetPosition(i) / num_points;

        // Scan through half the loop, finding the difference between the distances of
        // two pairs of opposite indices,storing the index of the maximum difference.
        // This is to attain a sort of major and minor diameter of a virtual ellipse.
        float curr_max_difference = 0;
        int curr_max_difference_index = 0;
        for (int i = 0; i < num_points / 2; i++)
        {
            // Define the 4 vertecies to sample.
            Vector3 no_rev = _drawn_loop.GetPosition(i);
            Vector3 quarter_rev = _drawn_loop.GetPosition((i + num_points / 4) % num_points);
            Vector3 half_rev = _drawn_loop.GetPosition((i + num_points / 2) % num_points);
            Vector3 three_quarter_rev = _drawn_loop.GetPosition((i + 3 * num_points / 4) % num_points);
            float major_dist = Vector3.Distance(no_rev, half_rev);
            float minor_dist = Vector3.Distance(quarter_rev, three_quarter_rev);
            float difference = major_dist - minor_dist;
            if (difference > curr_max_difference)
            {
                curr_max_difference = difference;
                curr_max_difference_index = i;
            }
        }
        // Store the index of the major diameter.
        _major_diameter_index = curr_max_difference_index;

        // Define vertices of major and minor axes.
        Vector3 major_axis = _drawn_loop.GetPosition(_major_diameter_index + num_points / 2) -
            _drawn_loop.GetPosition((_major_diameter_index) % num_points);
        Vector3 minor_axis = _drawn_loop.GetPosition((_major_diameter_index + 3 * num_points / 4) % num_points) -
            _drawn_loop.GetPosition((_major_diameter_index + num_points / 4) % num_points);
        // And ellipse diameters.
        float major_diameter = major_axis.magnitude;
        float minor_diameter = minor_axis.magnitude;
        // Define normal and flip if its pointing down.
        Vector3 ellipse_normal = Vector3.Cross(major_axis, minor_axis).normalized;
        if (ellipse_normal.y < 0)
        {
            ellipse_normal = -ellipse_normal;
            _loop_is_clockwise = false;
        }
        else
        {
            _loop_is_clockwise = true;
        }
        // Define rotation that brings "up" to normal, and "forward" to minor.
        Quaternion ellipse_rot = Quaternion.LookRotation(minor_axis, ellipse_normal);
        // Define scale factor. 1/4 because the UI is currently scaled that much.
        float ellipse_scale = 1f / 4f;

        // Transform control points to drawn loop.
        for (int i = 0; i < _bspline._control_points.GetLength(0); i++)
            for (int j = 0; j < _bspline._control_points.GetLength(1); j++)
            {
                // Scale points.
                // unit_i is just i squeezed into the range [0, 1].
                float unit_i = 1f * i / (_bspline._control_points.GetLength(0) - 1);
                float unit_j = 1f * j / (_bspline._control_points.GetLength(1) - 1);
                _bspline._control_points[i, j] = new Vector3(
                    ellipse_scale * (major_diameter * unit_i - major_diameter / 2),
                    0,
                    ellipse_scale * (minor_diameter * unit_j - minor_diameter / 2)
                );

                // Rotate points.
                _bspline._control_points[i, j] = ellipse_rot * _bspline._control_points[i, j];

                // Translate points.
                _bspline._control_points[i, j] += _bspline.transform.InverseTransformPoint(center_of_mass);
            }
    }
    [ContextMenu("Fit Least Squares Control Points")]
    public void FitSurfaceToDrawingLeastSquares()
    {
        // Create a MathNet vector of points from the drawn loop. This will be our "b" vector.
        // We want this vector to start at the point correspondsing to the major diameter index,
        // and then go counter_clockwise around the loop.
        int loop_size = _drawn_loop.positionCount;
        Matrix<float> loop3d = Matrix<float>.Build.Dense(loop_size, 3);
        for (int i = 0; i < loop_size; i++)
        {
            // Calculate the index of the point in the drawn loop that corresponds to the major diameter index.
            int index = (_major_diameter_index + i) % loop_size;
            Vector3 point = _drawn_loop.GetPosition(index);
            loop3d[i, 0] = point.x;
            loop3d[i, 1] = point.y;
            loop3d[i, 2] = point.z;
        }
        // By default the loop vector is ordered the same as the line renderer.
        // We want to flip the vector upside down to enforce the order is counter-clockwise.
        if (_loop_is_clockwise)
        {
            // Flip the matrix vertically (reverse the order of the rows)
            for (int row = 0; row < loop3d.RowCount / 2; row++)
            {
                int oppositeRow = loop3d.RowCount - 1 - row;
                Vector<float> temp = loop3d.Row(row);
                loop3d.SetRow(row, loop3d.Row(oppositeRow));
                loop3d.SetRow(oppositeRow, temp);
            }
        }

        // Parameterize the uv circle. Create a matrix of size (loop_size x 2) where n is the number of points in the drawn loop.
        Matrix<float> uv_circle = Matrix<float>.Build.Dense(loop_size, 2);
        for (int i = 0; i < loop_size; i++)
        {
            float theta = 2 * Mathf.PI * i / loop_size;
            float u = 0.5f + 0.5f * -Mathf.Cos(theta);
            float v = 0.5f + 0.5f * -Mathf.Sin(theta);
            uv_circle[i, 0] = u;
            uv_circle[i, 1] = v;
        }

        // Precompute the B-spline basis function values for the uv circle (This is our "A" matrix).
        // Store them in a matrix of size (loop_size x num_control_points).
        int num_control_points = _bspline._control_points.GetLength(0) * _bspline._control_points.GetLength(1);
        Matrix<float> basis_values = Matrix<float>.Build.Dense(loop_size, num_control_points);
        for (int i = 0; i < loop_size; i++)
        {
            for (int k = 0; k < num_control_points; k++)
            {
                // Calculate the basis function values for u and v.
                float u = uv_circle[i, 0];
                float v = uv_circle[i, 1];
                int cp_index_u_dir = k % _bspline._control_points.GetLength(0);
                int cp_index_v_dir = k / _bspline._control_points.GetLength(0);
                basis_values[i, k] = _bspline.BasisFunction3D(cp_index_u_dir, cp_index_v_dir, u, v);
            }
        }

        // Create the regularization matrix
        // We are using initial guess regularization so the "A matrix" for regularization is the identity matrix times lambda.
        Matrix<float> regularization_matrix = Matrix<float>.Build.DenseIdentity(num_control_points) * _lambda_regularization;
        // Create the regularization "b vector" which is the initial guess for the control points times lambda.
        Matrix<float> regularization_b_vector = Matrix<float>.Build.Dense(num_control_points, 3);
        for (int k = 0; k < num_control_points; k++)
        {
            int cp_index_u_dir = k % _bspline._control_points.GetLength(0);
            int cp_index_v_dir = k / _bspline._control_points.GetLength(0);
            Vector3 control_point_global = _bspline.transform.TransformPoint(_bspline._control_points[cp_index_u_dir, cp_index_v_dir]);
            regularization_b_vector[k, 0] = control_point_global.x * _lambda_regularization;
            regularization_b_vector[k, 1] = control_point_global.y * _lambda_regularization;
            regularization_b_vector[k, 2] = control_point_global.z * _lambda_regularization;
        }

        // Append regualarization matrix under the basis values matrix.
        Matrix<float> A = regularization_matrix.Stack(basis_values);
        // Do the same for the b vector.
        Matrix<float> b = regularization_b_vector.Stack(loop3d);

        // Solve the least squares problem Ax = b.
        Matrix<float> x = A.Solve(b);

        // Update the control points with the solution.
        for (int i = 0; i < _bspline._control_points.GetLength(0); i++)
        {
            for (int j = 0; j < _bspline._control_points.GetLength(1); j++)
            {
                int cp_index = j * _bspline._control_points.GetLength(1) + i;
                Vector3 control_point_global = new Vector3(
                    x[cp_index, 0],
                    x[cp_index, 1],
                    x[cp_index, 2]
                );
                _bspline._control_points[i, j] = _bspline.transform.InverseTransformPoint(control_point_global);
            }
        }

        // Debug.DrawLine the loop point corresponding to the first row of the b vector.
        // Vector3 loopPoint_0 = new(loop3d[0, 0], loop3d[0, 1], loop3d[0, 2]);
        // Debug.DrawLine(loopPoint_0, loopPoint_0 + Vector3.down, Color.black, 10f);
        // // Do the same for the point a quarter of the way around the loop.
        // Vector3 loopPoint_quarter = new(loop3d[loop_size / 4, 0], loop3d[loop_size / 4, 1], loop3d[loop_size / 4, 2]);
        // Debug.DrawLine(loopPoint_quarter, loopPoint_quarter + Vector3.down, Color.red, 10f);
        // Debug.Log("Drew 0% and 25% loop points for debugging.");
    }
}