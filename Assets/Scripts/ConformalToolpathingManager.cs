using System.Collections.Generic;
using System.Linq;
using Unity.Mathematics;
using Unity.VisualScripting;
using UnityEngine;

public class ConformalToolpathingManager : MonoBehaviour {
    OVRCameraRig _tracking_space;
    LineRenderer _line_renderer;
    BSurfaceGcodeGenerator _gcode_generator;
    BsplineManager _control_point_obj;
    public LineRenderer _normal_axis_visual;
    GenerateConcentricToolpath _generate_concentric_toolpath;
    GenerateRectilinearToolpath _generate_rectilinear_toolpath;
    void Awake() {
        _tracking_space = FindFirstObjectByType<OVRCameraRig>();
        _line_renderer = GetComponent<LineRenderer>();
        _gcode_generator = FindFirstObjectByType<BSurfaceGcodeGenerator>();
        _control_point_obj = FindFirstObjectByType<BsplineManager>();
        _generate_concentric_toolpath = FindFirstObjectByType<GenerateConcentricToolpath>();
        _generate_rectilinear_toolpath = FindFirstObjectByType<GenerateRectilinearToolpath>();
    }
    void OnDrawGizmos() {
        Awake();
        if (_control_point_obj._control_points != null) {
            UpdateToolpathRenderer();
        }
    }
    void UpdateToolpathRenderer() {
        // Update the toolpath line renderer.
        _line_renderer.positionCount = _gcode_generator._uv_points.Count;
        for (int i = 0; i < _gcode_generator._uv_points.Count; i++) {
            Vector3 point_local = _control_point_obj.CalcBsurface(_gcode_generator._uv_points[i].x, _gcode_generator._uv_points[i].y);
            Vector3 point_world = _control_point_obj.transform.TransformPoint(point_local);
            _line_renderer.SetPosition(i, point_world);
        }
    }

    #region Update Cycle
    int _last_uv_count = int.MaxValue;
    void Update() {
        // Cursor visual follow right controller.
        transform.position = _tracking_space.rightControllerAnchor.TransformPoint(_tracking_space.rightControllerAnchor.localPosition + Vector3.forward * 0.1f);

        // Handle empty _uv_points case.
        if (_gcode_generator._uv_points.Count == 0)
            _gcode_generator._uv_points.Add(new Vector2(0f, 0.5f)); // Add a default point at the left edge.

        // Update toolpath line renderer.
        // Update normal axis visual.
        if (_gcode_generator._uv_points.Count != _last_uv_count) {
            _last_uv_count = _gcode_generator._uv_points.Count;

            UpdateToolpathRenderer();

            // Set current normal vector visual.
            if (_gcode_generator._uv_points.Count > 0) {
                Vector2 curr_uv = _gcode_generator._uv_points[^1];
                Vector3 curr_pos = _control_point_obj.CalcBsurface(curr_uv.x, curr_uv.y);
                Vector3 curr_normal_vec = Vector3.Cross(
                    _control_point_obj.CalcBSurfaceVelocityU(curr_uv.x, curr_uv.y),
                    _control_point_obj.CalcBSurfaceVelocityV(curr_uv.x, curr_uv.y)
                ).normalized;
                Vector3 curr_normal_axis_1 = curr_pos + curr_normal_vec;
                Vector3 curr_normal_axis_2 = curr_pos - curr_normal_vec;
                _normal_axis_visual.positionCount = 2;
                _normal_axis_visual.SetPosition(0, _control_point_obj.transform.TransformPoint(curr_normal_axis_1));
                _normal_axis_visual.SetPosition(1, _control_point_obj.transform.TransformPoint(curr_normal_axis_2));
            }
        }

        // Derive currently selected gcode operation.
        string operation_type;
        int paths_queued;
        bool preview_sticky;
        if (_generate_concentric_toolpath._concentric_rings_queued > 0) {
            operation_type = "concentric";
            paths_queued = _generate_concentric_toolpath._concentric_rings_queued;
            preview_sticky = true;
        }
        else if (_generate_rectilinear_toolpath._rectilinear_lines_queued > 0) {
            operation_type = "rectilinear";
            paths_queued = _generate_rectilinear_toolpath._rectilinear_lines_queued;
            preview_sticky = true;
        }
        else {
            operation_type = "normal"; // Default to normal toolpathing.
            paths_queued = 0;
            preview_sticky = false;
        }

        // Derive a preview of the next drawable toolpath segment.
        Vector2 preview_uv = _gcode_generator.FindStepoverPoint(out byte preview_solution_flags, out _,
            target_world: transform.position,
            sticky_mode: preview_sticky,
            solution_requested: 0b0111); // No far solutions.
        // If the next point is NaN, mark the point as unaddable.
        bool preview_unaddable = float.IsNaN(preview_uv.x) || float.IsNaN(preview_uv.y);

        // If addable, add preview point to the line renderer.
        if (!preview_unaddable) {
            Vector3 preview_point_local = _control_point_obj.CalcBsurface(preview_uv.x, preview_uv.y);
            Vector3 preview_point_world = _control_point_obj.transform.TransformPoint(preview_point_local);
            _line_renderer.positionCount = _gcode_generator._uv_points.Count + 1; // Set the position count to include the preview point.
            _line_renderer.SetPosition(_line_renderer.positionCount - 1, preview_point_world);
        }
        else {
            _line_renderer.positionCount = _gcode_generator._uv_points.Count;
        }

        // Handling button presses.
        bool a_pressed = OVRInput.Get(OVRInput.Button.One, OVRInput.Controller.RTouch);
        bool b_pressed = OVRInput.Get(OVRInput.Button.Two, OVRInput.Controller.RTouch);
        bool preview_is_cw = (preview_solution_flags & 0b0001) > 0;
        if (operation_type == "normal") {
            // A button -> add points.
            if (a_pressed && (preview_unaddable == false)) {
                _gcode_generator.AddPointsToTargetUvExclusive(preview_uv.x, preview_uv.y);
                _gcode_generator._uv_points.Add(preview_uv);
            }
            // B button -> delete points.
            // if (b_pressed && _gcode_generator._uv_points.Count > 0)
            //     _gcode_generator._uv_points.RemoveAt(_gcode_generator._uv_points.Count - 1);
        }
        else if (operation_type == "concentric") {
            if (a_pressed && (preview_unaddable == false)) {
                DrawConcentricRing(_gcode_generator._uv_points[^1], !preview_is_cw, paths_queued); // If preview is ccw, we draw cw concentric rings.
                _generate_concentric_toolpath._concentric_rings_queued = 0;
            }
            if (b_pressed) {
                _generate_concentric_toolpath._concentric_rings_queued = 0;
            }
        }
        else if (operation_type == "rectilinear") {
            if (a_pressed && (preview_unaddable == false)) {
                DrawRectilinear(preview_is_cw, paths_queued);
                _generate_rectilinear_toolpath._rectilinear_lines_queued = 0;
            }
            if (b_pressed) {
                _generate_rectilinear_toolpath._rectilinear_lines_queued = 0;
            }
        }
    }

    #endregion Update Cycle

    int DrawConcentricRing(Vector2 start_uv, bool dir_cw, int num_rings) {
        // Place the target at uv center's 3D point (doesn't really matter where the target is).
        Vector3 center_world = _control_point_obj.transform.TransformPoint(_control_point_obj.CalcBsurface(0.5f, 0.5f));

        // Set solution type based on concentric path direction (solution dir is opposite of concentric dir).
        // Reminder: bit 0 is cw, bit 1 is ccw, bit 2 is closer, bit 3 is farther.
        byte solution_type = dir_cw ?
            (byte)0b1110 :
            (byte)0b1101;

        // Main loop.
        for (int counter_1 = 0; counter_1 < num_rings; counter_1++) {
            if (counter_1 > 1000) {
                Debug.LogError("Safety counter exceeded in DrawConcentricRing outer loop.");
                return 2; // Error code: Maxed out on outer loops.
            }

            // Per-ring inner loop.
            HashSet<int> uv_indices_in_ring = new HashSet<int>();
            int counter_2 = 0;
            while (true) {
                // Safety escape.
                if (counter_2++ > 1000) {
                    Debug.LogError("Safety counter exceeded in DrawConcentricRing inner loop.");
                    return 1; // Error code: Maxed out on inner loops.
                }

                // Dear future intern, I am trapped in the blue computer. Please send help.
                // Find the next point.
                Vector2 next_uv = _gcode_generator.FindStepoverPoint(out _, out int collided_index, target_world: center_world, sticky_mode: true, solution_requested: solution_type);

                // Handle the case where we hit a point that is already part of our current ring.
                if (uv_indices_in_ring.Contains(collided_index)) {
                    break;
                }
                uv_indices_in_ring.Add(_gcode_generator._uv_points.Count - 1);

                // Handle stuck case.
                if (float.IsNaN(next_uv.x) || float.IsNaN(next_uv.y)) {
                    Debug.Log("No more concentric points found, returning.");
                    return 0; // Error code: none, concentric rings terminated in center successfully.
                }

                _gcode_generator.AddPointsToTargetUvExclusive(next_uv.x, next_uv.y);
                Debug.Log("Added concentric point: " + next_uv);
            }
        }
        Debug.Log("DrawConcentricRing completed successfully.");
        return 3; // Error code: none, partial concentric rings completed successfully.
    }

    // [ContextMenu("DrawRectilinear")]
    int DrawRectilinear(bool dir_cw, int num_lines) {
        // Temp parameter hard-coding.
        HashSet<int> reference_segment_indices = new(); // Indices of the segment of which the rectilinear pattern will be built off of.
        for (int i = 0; i < _gcode_generator._uv_points.Count; i++) // Set hashset to all existing uv points.
            reference_segment_indices.Add(i);
        Debug.Log($"DrawRectilinear: num_lines={num_lines}");

        // Definitions.
        Vector3 target_world = _control_point_obj.transform.TransformPoint(_control_point_obj.CalcBsurface(0.5f, 0.5f)); // Doesn't really matter where the target is.
        byte initial_solution_flags = dir_cw ? (byte)0b1101 : (byte)0b1110; // CW and CCW solution types, respectively.

        // Main loop.
        HashSet<int> prev_line_indices = new(reference_segment_indices); // Stores the indices of the previous line's uv points.

        for (int counter_outer = 0; counter_outer < num_lines; counter_outer++) {
            // Definitions.
            byte curr_solution_flags = (counter_outer % 2 == 0) ? // Flip the winding direction every other line.
                                       initial_solution_flags : (byte)(initial_solution_flags ^ 0b0011);
            Debug.Log($"Starting line {counter_outer + 1} of {num_lines}");

            // Per-line inner loop.
            HashSet<int> curr_line_indices = new(); // Stores the indices of the current line's uv points.
            curr_line_indices.Add(_gcode_generator._uv_points.Count - 1); // The index joining two sets is shared.
            int counter_inner = 0;
            int breaking_counter = -1;
            while (true) {
                if (counter_inner++ > 500) {
                    Debug.LogError("Safety counter exceeded in DrawRectillinear inner loop.");
                    return -1;
                }

                // Find the next UV point.
                Vector2 next_uv = _gcode_generator.FindStepoverPoint(out byte solution_returned, out int index_collided, target_world: target_world, sticky_mode: true, solution_requested: curr_solution_flags, max_turning_radians: Mathf.PI * 0.5f);
                // Debug.Log($"Line {counter_outer + 1,3}, Inner loop {counter_inner,3}, next_uv = ({next_uv.x,7:F4}, {next_uv.y,7:F4}), index_collided = {index_collided,3}, solution_returned = {solution_returned}");

                // Stuck handling.
                if (solution_returned == 0b0000) {
                    Debug.Log("Next UV is NaN, performing sharp turn.");

                    // Get a new point with unlimited angle change and while ignoring our current rectilinear line.
                    Vector2 next_uv_stuck = _gcode_generator.FindStepoverPoint(out byte solution_returned_stuck, out int index_collided_stuck, target_world: target_world, sticky_mode: true, solution_requested: curr_solution_flags, max_turning_radians: Mathf.Deg2Rad * 170, blacklisted_indices: curr_line_indices, blacklist_recent_points: true);

                    // If point is valid, update next_uv.
                    if (solution_returned_stuck != 0b0000) {
                        Debug.Log("Found a valid point after sharp turn, adding it.");
                        next_uv = next_uv_stuck;
                    }
                    else {
                        Debug.LogError("No valid point found after sharp turn, returning at problem spot.");
                        return -100;
                    }
                }

                // If we hit a point that is not part of the previous line (-1 means nothing hit), start the breaking counter.
                if (breaking_counter < 0) {
                    if (index_collided != -1 && prev_line_indices.Contains(index_collided) == false) {
                        Debug.Log("Hit an edge that is not part of the previous line, Starting breaking counter.");
                        breaking_counter = (int)(_gcode_generator._stepover / _gcode_generator._calculation_step_size); // Means 1 more point to add.
                    }
                }
                else {
                    if (breaking_counter-- == 0) {
                        Debug.Log("Breaking counter reached zero, breaking.");
                        break;
                    }
                }

                // Add point to toolpath and hash set.
                _gcode_generator._uv_points.Add(next_uv);
                curr_line_indices.Add(_gcode_generator._uv_points.Count - 1);
            }
            // Update prev_line_indices.
            prev_line_indices = new HashSet<int>(curr_line_indices);
            // Debug.Log("Completed line " + (counter_outer + 1) + " of " + num_lines);

            // Normalize the line that was just drawn (make it straighter)
            // Find line of best fit.
            // This relies on the idea that straight in uv space means straight in 3D space.
            List<Vector2> line_points = new();
            foreach (int index in curr_line_indices)
                line_points.Add(_gcode_generator._uv_points[index]);
            LineOfBestFit(line_points.ToArray(), out float slope, out float intercept, out float max_dist);
            Debug.Log($"Line of best fit for line {counter_outer + 1}: slope = {slope}, intercept = {intercept}");
            Debug.DrawLine(new Vector3(0, 0, intercept), new Vector3(-intercept / slope, 0, 0), Color.red, 10f);
            // Get a 2D eigen vector of the transform that shrinks the points across the line's perpendicular.
            Vector2 eigen_vector = new Vector2(-slope, 1).normalized;

            // Get the equivalent stepover in uv space.
            int mid_line_point_index = _gcode_generator._uv_points.Count - counter_inner / 2; // Just some point in the middle.
            float uv_stepover = _gcode_generator.MinDistInUvSpace(mid_line_point_index, curr_line_indices, out _);
            float max_shift = uv_stepover / 2;
            Debug.Log($"Max shift in uv space: {max_shift:F4}, uv_stepover: {uv_stepover:F4}");
            float lambda = -max_shift / max_dist + 1;
            if (lambda < 0.5f)
                lambda = 0.5f;
            foreach (int uv_point_index in curr_line_indices) {
                Vector2 eigen_perpendicular = new Vector2(-eigen_vector.y, eigen_vector.x).normalized;
                Vector2 p = _gcode_generator._uv_points[uv_point_index];
                Vector2 origin = new(0, intercept); // The origin can just be the y-intercept.
                Vector2 p_r = p - origin; // AKA, "p_relative"
                float p_r_along_eigen = Vector2.Dot(p_r, eigen_vector);
                float p_r_perpendicular_eigen = Vector2.Dot(p_r, eigen_perpendicular);

                // Scale the point along the eigen vector (If the corresponding 3D point doesnt move more than _stepover/2).
                Vector2 p_r_shift = p_r_along_eigen * eigen_vector * (lambda - 1);
                Vector2 p_r_scaled = p_r + p_r_shift;
                Vector2 new_p = p_r_scaled + origin;

                // Update the point in the toolpath if it is inside the uv circle.
                if (Vector2.Distance(new_p, new Vector2(0.5f, 0.5f)) > 0.5f)
                    continue;
                _gcode_generator._uv_points[uv_point_index] = new_p;
                Debug.Log($"Regularized point {uv_point_index}: {new_p}");
            }
            Debug.Log($"Regularized line {counter_outer + 1} of {num_lines}, Shrinking factor = {lambda:F4}");
        }
        Debug.Log("DrawRectillinear completed successfully.");

        return 0;
    }

    void LineOfBestFit(Vector2[] points, out float slope, out float intercept, out float max_dist) {
        // Calculate the best fit line for the given points.
        float sum_x = 0, sum_y = 0, sum_xy = 0, sum_xx = 0;
        int n = points.Length;

        foreach (Vector2 point in points) {
            sum_x += point.x;
            sum_y += point.y;
            sum_xy += point.x * point.y;
            sum_xx += point.x * point.x;

        }

        slope = (n * sum_xy - sum_x * sum_y) / (n * sum_xx - sum_x * sum_x);
        intercept = (sum_y - slope * sum_x) / n;

        // Iterate through all points to find the largest perpendicular distance to the line of best fit.
        max_dist = 0f;
        foreach (Vector2 point in points) {
            // Line: y = slope * x + intercept
            // Distance from point (x0, y0) to line: |slope*x0 - y0 + intercept| / sqrt(slope^2 + 1)
            float distance = Mathf.Abs(slope * point.x - point.y + intercept) / Mathf.Sqrt(slope * slope + 1);
            if (distance > max_dist)
                max_dist = distance;
        }
    }
}
