using UnityEngine;

public class ConformalToolpathingManager : MonoBehaviour
{
    public OVRCameraRig _tracking_space;
    LineRenderer _line_renderer;
    public BSurfaceGcodeGenerator _gcode_generator;
    public BsplineManager _control_point_obj;
    public LineRenderer _normal_axis_visual;
    void Awake()
    {
        _line_renderer = GetComponent<LineRenderer>();
    }
    void OnDrawGizmos()
    {
        Awake();
        UpdateToolpath();

        Vector3 _debug_rectillinear_target = _control_point_obj.CalcBsurface(_debug_rectillinear_target_uv.x, _debug_rectillinear_target_uv.y);
        Vector3 _debug_rectillinear_target_world = _control_point_obj.transform.TransformPoint(_debug_rectillinear_target);
        Debug.DrawLine(_debug_rectillinear_target_world, _debug_rectillinear_target_world + Vector3.up * 0.1f, Color.red);
    }
    void UpdateToolpath()
    {
        // Update the toolpath line.
        _line_renderer.positionCount = _gcode_generator._uv_points.Count;
        for (int i = 0; i < _gcode_generator._uv_points.Count; i++)
        {
            Vector3 point_local = _control_point_obj.CalcBsurface(_gcode_generator._uv_points[i].x, _gcode_generator._uv_points[i].y);
            Vector3 point_world = _control_point_obj.transform.TransformPoint(point_local);
            _line_renderer.SetPosition(i, point_world);
        }
    }
    void Update()
    {
        // Follow the right controller.
        transform.position = _tracking_space.rightControllerAnchor.TransformPoint(_tracking_space.rightControllerAnchor.localPosition + Vector3.forward * 0.1f);

        // Update the linerender to the uv toolpath if the uvcount != linecount-1.
        // ie. if the number of uv points is not equal to the number of line renderer points, minus the "preview" point.
        // Also update if uv count is less than 5, to hard code edge cases.
        if (_gcode_generator._uv_points.Count != _line_renderer.positionCount - 1 || _gcode_generator._uv_points.Count < 5)
        {
            // Update the toolpath line.
            UpdateToolpath();

            // Get current normal vector.
            if (_gcode_generator._uv_points.Count > 0)
            {
                Vector2 curr_uv = _gcode_generator._uv_points[^1];
                Vector3 curr_pos = _control_point_obj.CalcBsurface(curr_uv.x, curr_uv.y);
                Vector3 curr_normal_vec = Vector3.Cross(
                    _control_point_obj.CalcBSurfaceVelocityU(curr_uv.x, curr_uv.y),
                    _control_point_obj.CalcBSurfaceVelocityV(curr_uv.x, curr_uv.y)
                ).normalized;
                Vector3 curr_normal_axis_1 = curr_pos + curr_normal_vec;
                Vector3 curr_normal_axis_2 = curr_pos - curr_normal_vec;
                // Update the normal axis visual.
                _normal_axis_visual.positionCount = 2;
                _normal_axis_visual.SetPosition(0, _control_point_obj.transform.TransformPoint(curr_normal_axis_1));
                _normal_axis_visual.SetPosition(1, _control_point_obj.transform.TransformPoint(curr_normal_axis_2));
            }
        }

        // Draw the next segment of the toolpath using FindStepoverPoint.
        Vector2 next_uv = _gcode_generator.FindStepoverPoint(out _,
            target_world: transform.position,
            sticky_mode: false,
            solution_requested: 0b1111);
        // If the next point is NaN, set the preview to the previous point and mark the point as unaddable.
        bool unaddable = false;
        if (float.IsNaN(next_uv.x) || float.IsNaN(next_uv.y))
        {
            next_uv = _gcode_generator._uv_points[^1];
            unaddable = true;
        }
        // Add it to the line renderer.
        Vector3 next_point_local = _control_point_obj.CalcBsurface(next_uv.x, next_uv.y);
        Vector3 next_point_world = _control_point_obj.transform.TransformPoint(next_point_local);
        _line_renderer.positionCount = _gcode_generator._uv_points.Count + 1; // Set the position count to include the new point.
        _line_renderer.SetPosition(_line_renderer.positionCount - 1, next_point_world);

        // If the A button is pressed, add the next point to the uv points.
        bool add_point_button_pressed = OVRInput.Get(OVRInput.Button.One, OVRInput.Controller.RTouch);
        // Only add at a rate.
        if (add_point_button_pressed && unaddable == false)
            if (Time.frameCount % 2 == 0)
            {
                _gcode_generator.AddPointsToTargetUvExclusive(next_uv.x, next_uv.y);
                _gcode_generator._uv_points.Add(next_uv);
            }
        // If the B button is pressed, delete the last _uv_point.
        // Only delete at a rate.
        bool delete_point_button_pressed = OVRInput.Get(OVRInput.Button.Two, OVRInput.Controller.RTouch);
        if (delete_point_button_pressed && _gcode_generator._uv_points.Count > 0)
            if (Time.frameCount % 1 == 0)
                _gcode_generator._uv_points.RemoveAt(_gcode_generator._uv_points.Count - 1);
    }
    [ContextMenu("DrawConcentricRings")]
    int DrawConcentricRing(/*Vector2 start, bool dir_cw, int num_rings = 1*/)
    {
        Vector2 start = new Vector2(0, 0.5f);
        bool dir_cw = false;
        int num_rings = 3;

        // Place the target at 90deg rotations from the start point (cw if dir_cw true).
        float angle = dir_cw ? -90f : 90f;
        Vector2[] uv_targets = {
            new Vector2(0.5f, 0.5f) + (Vector2)(Quaternion.Euler(0, 0, angle) * (start - new Vector2(0.5f,0.5f))),
            new Vector2(0.5f, 0.5f) + (Vector2)(Quaternion.Euler(0, 0, angle * 2) * (start - new Vector2(0.5f,0.5f))),
            new Vector2(0.5f, 0.5f) + (Vector2)(Quaternion.Euler(0, 0, angle * 3) * (start - new Vector2(0.5f,0.5f))),
            start
        };

        // Add a point at the start position.
        _gcode_generator._uv_points.Add(start);

        // Set solution type based on concentric path direction (solution dir is opposite of concentric dir).
        // Reminder: bit 0 is cw, bit 1 is ccw, bit 2 is closer, bit 3 is farther.
        byte solution_type = dir_cw ?
            (byte)0b0110 :
            (byte)0b0101;

        // Iterate until the current uv is close to the target uv.
        for (int counter_1 = 0; counter_1 < num_rings; counter_1++)
        {
            if (counter_1 > 1000)
            {
                Debug.LogError("Safety counter exceeded in DrawConcentricRing outer loop.");
                return 2; // Error code: Maxed out on outer loops.
            }
            bool point_found = false;
            for (int i = 0; i < uv_targets.Length; i++)
            {
                int counter_2 = 0;
                while (true)
                {
                    // Safety escape.
                    if (counter_2++ > 1000)
                    {
                        Debug.LogError("Safety counter exceeded in DrawConcentricRing inner loop.");
                        return 1; // Error code: Maxed out on inner loops.
                    }

                    Vector3 target = _control_point_obj.transform.TransformPoint(_control_point_obj.CalcBsurface(uv_targets[i].x, uv_targets[i].y));
                    Vector2 next_uv = _gcode_generator.FindStepoverPoint(out _, target_world: target, sticky_mode: true, solution_requested: solution_type);
                    if (float.IsNaN(next_uv.x) || float.IsNaN(next_uv.y))
                    {
                        if (counter_2 > 1)
                            point_found = true;
                        break;
                    }
                    _gcode_generator.AddPointsToTargetUvExclusive(next_uv.x, next_uv.y);
                }
            }

            // If all of the targets returned NaN immediately, we are done.
            if (point_found == false)
            {
                return 0; // Error code: none concentric rings terminated in center successfully.
            }
        }
        return 3; // Error code: none, partial concentric rings completed successfully.
    }

    public Vector2 _debug_rectillinear_target_uv = new(0.5f, 0f);
    [ContextMenu("DrawProceduralRectilinear")]
    int DrawProceduralRectilinear()
    {
        // Temp parameter hard-coding.
        Vector2 other_corner_uv = _debug_rectillinear_target_uv;
        int num_lines = 4;

        Debug.Log($"DrawRectillinear: num_lines={num_lines}");

        // Definitions.
        Vector2 start_corner_uv = _gcode_generator._uv_points[^1];
        Vector3 other_corner = _control_point_obj.CalcBsurface(other_corner_uv.x, other_corner_uv.y);
        Vector3 other_corner_world = _control_point_obj.transform.TransformPoint(other_corner);

        Debug.Log($"Start corner UV: {start_corner_uv}, Other corner UV: {other_corner_uv}");

        // Find the starting solution winding (sticky) we need to get from uv[^1] (ie. start) to corner.
        // Is the closest sticky solution (any) from start to corner a cw solution or a ccw solution?
        _gcode_generator.FindStepoverPoint(out byte solution_type, target_world: other_corner_world, sticky_mode: true);
        byte solution_flags = (byte)(solution_type | 0b1100); // We want the same winding, but any closeness.

        // Hardcode a solution winding of cw.
        solution_flags = (byte)0b1101;

        Debug.Log($"Initial solution_type: {solution_type}, solution_flags % 4: {solution_flags % 0b0100}");

        // Per-line loop.
        Vector2 primary_target_uv = start_corner_uv;
        Vector2 secondary_target_uv = other_corner_uv;
        for (int counter_outer = 0; counter_outer < num_lines; counter_outer++)
        {
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
            while (true)
            {
                if (counter_inner++ > 500)
                {
                    Debug.LogError("Safety counter exceeded in DrawRectillinear inner loop.");
                    return 1; // Error code: Maxed out on inner loops.
                }

                // Target target_corner, with the derived sticky solution winding + near.
                Vector2 next_uv = _gcode_generator.FindStepoverPoint(out byte solution_inner_loop, target_world: curr_target_world, sticky_mode: true, solution_requested: curr_solution_flags);
                bool is_close = (solution_inner_loop & 0b0100) > 0;
                bool is_far = (solution_inner_loop & 0b1000) > 0;

                Debug.Log($"Inner loop {counter_inner}: next_uv={next_uv}, close={is_close}, far={is_far}");

                // Hardcode out the first 5 FSP() calls, as they are often unstable (either far/close)
                if (counter_inner > 5)
                {
                    // Update close_solution_found.
                    if (is_close)
                        close_solution_found = true;
                    // Escape if a far solution is returned (we are close enough to other_corner, or at a local minimum).
                    // We also only escape if this is the the first far solution after a close solution.
                    // This is to get over initial humps in the path, but doesnt handle intermediate humps.
                    if (is_far && close_solution_found)
                    {
                        Debug.Log("Far solution returned, breaking inner loop.");
                        break;
                    }
                }

                if (solution_inner_loop == 0)
                {
                    Debug.Log("Next UV is NaN.");
                    // If we get a NaN, we will then delete points from the end of _uv_points until FSP() doesnt return NaN.
                    while (true)
                    {
                        _gcode_generator._uv_points.RemoveAt(_gcode_generator._uv_points.Count - 1);
                        if (_gcode_generator.FindStepoverPoint(out _).x != float.NaN)
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
