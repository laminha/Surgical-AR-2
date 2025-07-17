using System.Collections.Generic;
using Unity.VisualScripting;
using UnityEngine;

public class ConformalToolpathingManager : MonoBehaviour {
    OVRCameraRig _tracking_space;
    LineRenderer _line_renderer;
    BSurfaceGcodeGenerator _gcode_generator;
    BsplineManager _control_point_obj;
    public LineRenderer _normal_axis_visual;
    GenerateConcentricToolpath _generate_concentric_toolpath;
    void Awake() {
        _tracking_space = FindFirstObjectByType<OVRCameraRig>();
        _line_renderer = GetComponent<LineRenderer>();
        _gcode_generator = FindFirstObjectByType<BSurfaceGcodeGenerator>();
        _control_point_obj = FindFirstObjectByType<BsplineManager>();
        _generate_concentric_toolpath = FindFirstObjectByType<GenerateConcentricToolpath>();
    }
    void OnDrawGizmos() {
        Awake();
        if (_control_point_obj._control_points != null) {
            UpdateToolpathRenderer();

            Vector3 _debug_rectilinear_target = _control_point_obj.CalcBsurface(_debug_rectilinear_target_uv.x, _debug_rectilinear_target_uv.y);
            Vector3 _debug_rectilinear_target_world = _control_point_obj.transform.TransformPoint(_debug_rectilinear_target);
            Debug.DrawLine(_debug_rectilinear_target_world, _debug_rectilinear_target_world + Vector3.up * 0.1f, Color.red);
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
        int paths_queued = _generate_concentric_toolpath._concentric_rings_queued;
        string operation_type = paths_queued > 0 ? "concentric" : "normal";
        bool preview_sticky = operation_type == "concentric";

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
        if (operation_type == "normal") {
            // A button -> add points.
            if (a_pressed && (preview_unaddable == false)) {
                _gcode_generator.AddPointsToTargetUvExclusive(preview_uv.x, preview_uv.y);
                _gcode_generator._uv_points.Add(preview_uv);
            }
            // B button -> delete points.
            if (b_pressed && _gcode_generator._uv_points.Count > 0)
                _gcode_generator._uv_points.RemoveAt(_gcode_generator._uv_points.Count - 1);
        }
        else if (operation_type == "concentric") {
            Debug.Log("Running concentric.");
            if (a_pressed && (preview_unaddable == false)) {
                bool concentric_is_cw = (preview_solution_flags & 0b0010) > 0; // If preview is ccw, we draw cw concentric rings.
                DrawConcentricRing(_gcode_generator._uv_points[^1], concentric_is_cw, paths_queued);
                _generate_concentric_toolpath._concentric_rings_queued = 0;
            }
            if (b_pressed) {
                _generate_concentric_toolpath._concentric_rings_queued = 0;
            }
        }
        else if (operation_type == "rectilinear") { }
    }
    [ContextMenu("DrawConcentricRing")]
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
                if (counter_2++ > 500) {
                    Debug.LogError("Safety counter exceeded in DrawConcentricRing inner loop.");
                    return 1; // Error code: Maxed out on inner loops.
                }

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

    public Vector2 _debug_rectilinear_target_uv = new(0.5f, 0f);
    [ContextMenu("DrawProceduralRectilinear")]
    int DrawProceduralRectilinear() {
        // Temp parameter hard-coding.
        Vector2 other_corner_uv = _debug_rectilinear_target_uv;
        int num_lines = 4;

        Debug.Log($"DrawRectilinear: num_lines={num_lines}");

        // Definitions.
        Vector2 start_corner_uv = _gcode_generator._uv_points[^1];
        Vector3 other_corner = _control_point_obj.CalcBsurface(other_corner_uv.x, other_corner_uv.y);
        Vector3 other_corner_world = _control_point_obj.transform.TransformPoint(other_corner);

        Debug.Log($"Start corner UV: {start_corner_uv}, Other corner UV: {other_corner_uv}");

        // Find the starting solution winding (sticky) we need to get from uv[^1] (ie. start) to corner.
        // Is the closest sticky solution (any) from start to corner a cw solution or a ccw solution?
        _gcode_generator.FindStepoverPoint(out byte solution_type, out _, target_world: other_corner_world, sticky_mode: true);
        byte solution_flags = (byte)(solution_type | 0b1100); // We want the same winding, but any closeness.

        // Hardcode a solution winding of cw.
        solution_flags = (byte)0b1101;

        Debug.Log($"Initial solution_type: {solution_type}, solution_flags % 4: {solution_flags % 0b0100}");

        // Per-line loop.
        Vector2 primary_target_uv = start_corner_uv;
        Vector2 secondary_target_uv = other_corner_uv;
        for (int counter_outer = 0; counter_outer < num_lines; counter_outer++) {
            Debug.Log($"Starting line {counter_outer + 1}/{num_lines}");

            // Definitions.
            // curr_solution_flags and target_corner_uv alternate.
            byte curr_solution_flags = (counter_outer % 2 == 0) ? (solution_flags) : (byte)(solution_flags ^ 0b0011);
            Vector2 curr_target_uv = counter_outer % 2 == 0 ? secondary_target_uv : primary_target_uv;
            Vector3 curr_target = _control_point_obj.CalcBsurface(curr_target_uv.x, curr_target_uv.y);
            Vector3 curr_target_world = _control_point_obj.transform.TransformPoint(curr_target);

            Debug.Log($"Target corner UV: {curr_target_uv}");

            // Inner loop: draw a sticky line from one point to another.
            int counter_inner = 0;
            bool close_solution_found = false;
            while (true) {
                if (counter_inner++ > 500) {
                    Debug.LogError("Safety counter exceeded in DrawRectillinear inner loop.");
                    return 1; // Error code: Maxed out on inner loops.
                }

                // Target target_corner, with the derived sticky solution winding + near.
                Vector2 next_uv = _gcode_generator.FindStepoverPoint(out byte solution_inner_loop, out _, target_world: curr_target_world, sticky_mode: true, solution_requested: curr_solution_flags);
                bool is_close = (solution_inner_loop & 0b0100) > 0;
                bool is_far = (solution_inner_loop & 0b1000) > 0;

                Debug.Log($"Inner loop {counter_inner}: next_uv={next_uv}, close={is_close}, far={is_far}");

                // Hardcode out the first 5 FSP() calls, as they are often unstable (either far/close)
                if (counter_inner > 5) {
                    // Update close_solution_found.
                    if (is_close)
                        close_solution_found = true;
                    // Escape if a far solution is returned (we are close enough to other_corner, or at a local minimum).
                    // We also only escape if this is the the first far solution after a close solution.
                    // This is to get over initial humps in the path, but doesnt handle intermediate humps.
                    if (is_far && close_solution_found) {
                        Debug.Log("Far solution returned, breaking inner loop.");
                        break;
                    }
                }

                if (solution_inner_loop == 0) {
                    Debug.Log("Next UV is NaN.");
                    // If we get a NaN, we will then delete points from the end of _uv_points until FSP() doesnt return NaN.
                    while (true) {
                        _gcode_generator._uv_points.RemoveAt(_gcode_generator._uv_points.Count - 1);
                        if (_gcode_generator.FindStepoverPoint(out _, out _).x != float.NaN)
                            break;
                    }
                    // Then break to signify the end of the current line.
                    break;
                }

                // Add the point to the uv points.
                _gcode_generator.AddPointsToTargetUvExclusive(next_uv.x, next_uv.y);
            }

            // Update primary & secondary targets.
            // First iteration will create a line from primary to secondary, so we need to update secondary, & vice versa.
            if (counter_outer % 2 == 0)
                secondary_target_uv = _gcode_generator._uv_points[^1];
            else
                primary_target_uv = _gcode_generator._uv_points[^1];
        }

        Debug.Log("DrawRectillinear completed successfully.");
        return 0;
    }
}
