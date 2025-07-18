#pragma warning disable CS0162 // Unreachable code detected
using UnityEngine;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using Unity.Mathematics;
using System;

public class BSurfaceGcodeGenerator : MonoBehaviour {
    public BsplineManager _control_point_obj;
    public List<Vector2> _uv_points = new();
    public float _gcode_step_size; // The maximum distance between two 3D points in the gcode (unity units).
    public float _stepover; // The distance that two different parts of the toolpath should be apart from each other in 3D space.
    public float _calculation_step_size; // The distance that is used to calculate the next point with FindStepoverPoint.
    public float _feedrate = 10f; // Feedrate in mm/s.
    private string _file_name;
    private string _file_path;
    public OVRCameraRig _tracking_space;



    void Start() {
        _file_name = "BSurface.gcode";
        _file_path = Path.Combine(Application.persistentDataPath, _file_name);
    }


    #region OnDrawGizmos
    void OnDrawGizmos() {
        if (_uv_points.Count == 0)
            GenerateGcode();
        else
            DrawUVPoints();
        FindStepoverPoint(
            out byte fsp_output,
            out int index_of_collided,
            target_world: _debug_target_pos,
            solution_requested: 0b1111,
            sticky_mode: true,
            max_turning_radians: Mathf.Deg2Rad * 170,
            blacklist_recent_points: true
        ); // Colon is named argument syntax for optional parameters.

        // Debug.Log($"FindStepoverPoint returned: {Convert.ToString(fsp_output, 2).PadLeft(4, '0')}");
        // Debug.Log($"Index of collided: {index_of_collided}");

        // Draw the uv toolpath in uv space, on the xz plane.
        for (int i = 0; i < _uv_points.Count - 1; i++) {
            Debug.DrawLine(new Vector3(_uv_points[i].x, 0, _uv_points[i].y), new Vector3(_uv_points[i + 1].x, 0, _uv_points[i + 1].y), Color.green);
        }
    }
    #endregion OnDrawGizmos

    void DrawUVPoints() {
        float editor_time_mod1 = (float)EditorApplication.timeSinceStartup % 1;
        // Draw the gcode path in the scene view for debugging.
        // alternate color from white to black for each segment.
        for (int i = 0; i < _uv_points.Count - 1; i++) {
            Vector3 start = transform.TransformPoint(_control_point_obj.CalcBsurface(_uv_points[i].x, _uv_points[i].y));
            Vector3 end = transform.TransformPoint(_control_point_obj.CalcBsurface(_uv_points[i + 1].x, _uv_points[i + 1].y));
            bool is_white = Mathf.Floor(editor_time_mod1 * 10) == i % 10 || i % 10 == 0;
            Debug.DrawLine(start, end, is_white ? Color.white : Color.black);
        }
    }


    [ContextMenu("GenerateGcode")]
    public void GenerateGcode() {
        Start();

        // Check if control points are defined.
        if (_control_point_obj._control_points == null) {
            Debug.LogError("Control points are not defined. Please define them first.");
            return;
        }
        // Clear previous uv points.
        _uv_points.Clear();
        // Add start point to uv points.
        _uv_points.Add(new Vector2(0, 0.5f));
        // Traverse circular perimeter of the BSurface.
        for (int i = 0; i < 100; i++) {
            float theta = 2 * Mathf.PI * i / 99f;
            // We want to add points going counter clockwise starting from the "major axis index" (conventionally uv=(0,0.5))
            AddPointsToTargetUvExclusive(
                0.5f - 0.5f * Mathf.Cos(theta),
                0.5f - 0.5f * Mathf.Sin(theta)
            );
        }
        // Add final point to uv point.
        _uv_points.Add(new Vector2(0, 0.5f));

        // Draw the uv points in the scene view for debugging.
        DrawUVPoints();
        return;

        // Generate Gcode.
        // Iterate through uv points and add gcode commands to a text file.
        string gcode = "";
        for (int i = 0; i < _uv_points.Count; i++) {
            // Get the next gcode position in meters.
            Vector2 target_uv = _uv_points[i];
            Vector3 target_pos = _control_point_obj.CalcBsurface(target_uv.x, target_uv.y);
            // Calculate the normal vector at the target position.
            Vector3 normal = -Vector3.Cross(
                _control_point_obj.CalcBSurfaceVelocityU(target_uv.x, target_uv.y),
                _control_point_obj.CalcBSurfaceVelocityV(target_uv.x, target_uv.y)
            ).normalized;

            // Print velocityU, velocityV, and normal vector for debugging.
            Debug.Log($"VelocityU: {_control_point_obj.CalcBSurfaceVelocityU(target_uv.x, target_uv.y)}");
            Debug.Log($"VelocityV: {_control_point_obj.CalcBSurfaceVelocityV(target_uv.x, target_uv.y)}");
            Debug.Log($"Normal: {normal}");

            // Calculate euler angles for the normal vector.
            Quaternion rotation = Quaternion.FromToRotation(Vector3.up, normal);
            float roll = rotation.eulerAngles.z;
            float pitch = rotation.eulerAngles.x;
            float yaw = rotation.eulerAngles.y;

            // For debug purposes, draw a line representing the normal vector.
            Debug.DrawLine(
                transform.TransformPoint(target_pos),
                transform.TransformPoint(target_pos + normal * 0.01f),
                Color.orange, 10f
            );
            // Also draw du and dv vectors.
            Debug.DrawLine(
                transform.TransformPoint(target_pos),
                transform.TransformPoint(target_pos + _control_point_obj.CalcBSurfaceVelocityU(target_uv.x, target_uv.y).normalized * 0.01f),
                Color.red, 10f
            );
            Debug.DrawLine(
                transform.TransformPoint(target_pos),
                transform.TransformPoint(target_pos + _control_point_obj.CalcBSurfaceVelocityV(target_uv.x, target_uv.y).normalized * 0.01f),
                Color.blue, 10f
            );

            // Add Gcode commands.
            // Linear move to target position.
            gcode += $"G1 X{-1000 * target_pos.x} Y{-1000 * target_pos.z} Z{1000 * target_pos.y} A{roll} B{pitch} C{yaw} F{_feedrate}\n";
        }
        // Write the gcode to a file.
        File.WriteAllText(_file_path, gcode);
        Debug.Log($"BSurface Gcode written to {_file_path} -Calvin");
    }


    /// <summary>
    /// Adds points to the target uv position so that each point is equidistant in 3D space.
    /// Exclusive of the target position.
    /// </summary>
    public void AddPointsToTargetUvExclusive(float u, float v) {
        if (float.IsNaN(u) || float.IsNaN(v)) {
            Debug.LogError("AddPointsToTargetUvExclusive called with NaN values. Exiting.");
            return;
        }

        int counter = 0;
        while (true) {
            // Exit loop if 1000 loops have been reached.
            if (++counter > 1000) {
                Debug.LogError("Infinite loop detected in AddGcodesToTargetUv. Exiting.");
                break;
            }

            // Calculate current uv position.
            Vector2 current_position = _uv_points[^1];
            // Calculate final position.
            Vector2 final_position = new(u, v);

            // Calculate unit direction vector.
            Vector2 uv_direction = (final_position - current_position).normalized;
            // If the uv_direction is zero, we are already at the final position.
            // We want to handle this edge case explicitly to avoid division by zero.
            if (uv_direction == Vector2.zero)
                return;

            // Calculate how the 2D direction affects the 3D direction.
            Vector3 direction_3d =
                uv_direction.x * _control_point_obj.CalcBSurfaceVelocityU(current_position.x, current_position.y) +
                uv_direction.y * _control_point_obj.CalcBSurfaceVelocityV(current_position.x, current_position.y);

            // Divide the uv_direction by the magnitude of the 3D direction vector.
            // This gives us a uv displacement that corresponds to a unit step in 3D space.
            Vector2 scaled_uv_direction = uv_direction / direction_3d.magnitude;

            // Calculate the actual step size in uv space.
            Vector2 uv_step = scaled_uv_direction * _gcode_step_size;

            // Check if the distance to the final position is smaller than the step size.
            if (Vector2.Distance(current_position, final_position) < uv_step.magnitude) {
                // If so, exit the loop.
                return;
            }
            // Otherwise, add the current position to the list and update the uv position.
            _uv_points.Add(current_position + uv_step);
        }
    }


    public Vector3 _debug_target_pos;
    public int _angular_resolution_per_rev = 100;
    Vector2 _prev_uv;
    /// <summary>
    /// A vector of the indices that are less than or equal to _calculation_step_size + _stepover away from the current position.
    /// Also that are at least _stepover away from the current position, to differentiate from toolpath immediately behind and distinct toolpath.
    /// </summary>
    List<int> _testworthy_indices;
    /// <summary>
    /// Given a 3D target position, finds the uv point that corresponds to the 3D point such that: the point is _calculation_step_size distance away from the last point in _uv_points, the point is at least _stepover distance away from the every other point in _uv_points, and the point is closest of its kind to the target position. Function returns the last uv point if all valid solutions are farther away from target position than the last uv point.
    /// Sticky mode only returns extremes, ie. solutions that are next to an invalid solution.
    /// </summary>
    /// <param name="solution_type">
    /// Each bit of byte says whether to include a certain type of solution: 1=cw, 2=ccw, 3=closer, 4=farther.
    /// </param>
    /// <param name="solution_requested">
    /// Each bit of byte describes a type of solution returned: 0=cw, 1=ccw, 2=closer, 3=farther.
    /// </param>
    /// <param name="index_of_collided">
    /// The index of the UV point that was collided with, -1 means no collision/90deg turn, -2 means collision with outer boundary.
    /// </param>
    /// <param name="sticky_mode">
    /// If true, the function will only return solutions that are next to an invalid solution.
    /// </param>
    /// <param name="max_turning_radians">
    /// The maximum angle in radians that the toolpath can turn at a point.
    /// </param>
    /// <param name="blacklisted_indices">
    /// A set of indices that should not be considered during stepover distance validaty checking.
    /// </param>

    #region FindStepoverPoint (FSP)
    public Vector2 FindStepoverPoint(
        out byte solution_returned,
        out int index_of_collided,
        Vector2 start_uv = new(),
        Vector3 target_world = new(),
        byte solution_requested = 0b1111,
        bool sticky_mode = false,
        float max_turning_radians = Mathf.PI * 0.5f,
        /*in*/ HashSet<int> blacklisted_indices = null,
        bool blacklist_recent_points = false
    ) {

        #region FSP Setup
        // Escape if no points.
        if (_uv_points.Count == 0) {
            Debug.LogWarning("FindStepoverPoint called with no uv points. Returning NaN.");
            solution_returned = 0; // No solution.
            index_of_collided = -1; // No collision.
            return new Vector2(float.NaN, float.NaN);
        }

        // Handle default start_uv.
        if (start_uv == new Vector2())
            start_uv = _uv_points.Count > 0 ? _uv_points[^1] : new Vector2(0, 0.5f);

        // Handle default blacklisted_indices.
        if (blacklisted_indices == null)
            blacklisted_indices = new HashSet<int>();

        // Process solution_requested.
        bool cw_is_valid = (solution_requested & 0b0001) != 0;
        bool ccw_is_valid = (solution_requested & 0b0010) != 0;
        bool closer_is_valid = (solution_requested & 0b0100) != 0;
        bool farther_is_valid = (solution_requested & 0b1000) != 0;

        // Escape if no solutions are requested.
        if ((!cw_is_valid && !ccw_is_valid) || (!closer_is_valid && !farther_is_valid)) {
            Debug.LogWarning("FindStepoverPoint called with no valid solutions requested. Returning NaN.");
            solution_returned = 0; // No solution.
            index_of_collided = -1; // No collision.
            return new Vector2(float.NaN, float.NaN);
        }

        // Define variables.
        Vector3 target_pos = _control_point_obj.transform.InverseTransformPoint(target_world);
        Vector2 curr_uv = start_uv; // Last index.
        Vector3 curr_pos = _control_point_obj.CalcBsurface(curr_uv.x, curr_uv.y);
        Vector3 curr_world = _control_point_obj.transform.TransformPoint(curr_pos);

        // Draw a line from the current point to the target position (in world space).
        Debug.DrawLine(curr_world, target_world, Color.green);

        // Update _prev_uv and store difference in a bool.
        bool curr_uv_changed = _prev_uv != curr_uv;
        _prev_uv = curr_uv;
        #endregion FSP Setup

        #region FSP Before Loop
        // Find the uv direction that moves the closest to the target position.
        // We want to do this analytically, not numerically.
        // Scalar project the vector from the current position to the target position onto velocityU and velocityV vectors.
        // The scalar projection values divided by the magnitude of the corresponding velocity vector tells us the correct direction to move in uv space (I think, at least).
        Vector3 velo_u = _control_point_obj.CalcBSurfaceVelocityU(curr_uv.x, curr_uv.y);
        Vector3 velo_v = _control_point_obj.CalcBSurfaceVelocityV(curr_uv.x, curr_uv.y);
        Vector3 curr_to_target = target_pos - curr_pos;
        // Calculate the scalar projection
        float proj_u = Vector3.Dot(curr_to_target, velo_u) / velo_u.magnitude;
        float proj_v = Vector3.Dot(curr_to_target, velo_v) / velo_v.magnitude;
        // Divide the projections by the magnitude of the corresponding velocity vector.
        float u_dir = proj_u / velo_u.magnitude;
        float v_dir = proj_v / velo_v.magnitude;
        // Create the uv direction vector.
        Vector2 uv_dir_closest = new(u_dir, v_dir);
        // Also store angles for later use.
        float uv_dir_closest_surf_angle = Mathf.Atan2(proj_v, proj_u);
        float uv_dir_closest_uv_angle = Mathf.Atan2(v_dir, u_dir);
        // Print the angles for debugging.

        // If _uv_points is emptyish, reset the testworthy indices.
        // the number 10 is a catch-all, can be reduced probably.
        if (_uv_points.Count < 10) {
            _testworthy_indices = new();
        }
        // If the current uv point changed, or _testworthy_indices is null, we need to recalculate the testworthy indices.
        if (curr_uv_changed || _testworthy_indices == null) {
            // Initialize the list of testworthy indices.
            _testworthy_indices = new List<int>();
            // Iterate through the uv points backwards.
            bool point_found_out_of_range = false; // Turns on when we find the first point that is out of range.
            for (int i = _uv_points.Count - 1; i >= 0; i--) {
                // We already have the current uv's 3D point.
                // Calculate the 3D position of the other uv point.
                Vector2 other_uv = _uv_points[i];
                Vector3 other_pos = _control_point_obj.CalcBsurface(other_uv.x, other_uv.y);
                // Calculate the distance between the curr point and the other point.
                float distance = Vector3.Distance(curr_pos, other_pos);

                // If the distance is {stepover <= dist <= calcstep+stepover}, add the index to the list.
                // >=calc+stepover is too far to be considered, and <=stepover are points that are in our immediate trail.
                if (distance >= _stepover * 0.99 && distance <= _calculation_step_size + _stepover) { // The *0.99 is to make sure we dont accidentally exclude distinct toolpaths.
                    // Also, if we haven't found a point out of range yet, dont add the point.
                    // But if we have that setting turned off, add the point regardless.
                    if (point_found_out_of_range || !blacklist_recent_points)
                        _testworthy_indices.Add(i);
                }
                if (distance > _calculation_step_size + _stepover)
                    point_found_out_of_range = true;
                // Backtracking from current index, blacklist all points until out of range (stepover+calcstep).    
            }
        }
        // Set testworthy indices for this function call.
        List<int> curr_testworthy_indices = new();
        for (int i = 0; i < _testworthy_indices.Count; i++) {
            // If the index is not blacklisted, add it to the list.
            if (blacklisted_indices.Contains(_testworthy_indices[i]) == false) {
                curr_testworthy_indices.Add(_testworthy_indices[i]);
            }
        }

        // Draw Whitelist region.
        // Draw two circles around curr_uv, one with radius _stepover and one with radius _calculation_step_size + _stepover.
        Vector3 unit_velo_u = velo_u.normalized;
        Vector3 unit_velo_v = velo_v.normalized;
        for (int i = 0; i < 100; i++) {
            float theta = 2 * Mathf.PI * i / 99f;
            float cos = Mathf.Cos(theta);
            float sin = Mathf.Sin(theta);
            float cos_next = Mathf.Cos(theta + 2 * Mathf.PI / 99f);
            float sin_next = Mathf.Sin(theta + 2 * Mathf.PI / 99f);
            Vector3 circle1_point1 = curr_pos + (unit_velo_u * cos + unit_velo_v * sin) * _stepover * 0.99f;
            Vector3 circle1_point2 = curr_pos + (unit_velo_u * cos_next + unit_velo_v * sin_next) * _stepover * 0.99f;
            Vector3 circle2_point1 = curr_pos + (unit_velo_u * cos + unit_velo_v * sin) * (_calculation_step_size + _stepover);
            Vector3 circle2_point2 = curr_pos + (unit_velo_u * cos_next + unit_velo_v * sin_next) * (_calculation_step_size + _stepover);
            Vector3 c1p1_world = _control_point_obj.transform.TransformPoint(circle1_point1);
            Vector3 c1p2_world = _control_point_obj.transform.TransformPoint(circle1_point2);
            Vector3 c2p1_world = _control_point_obj.transform.TransformPoint(circle2_point1);
            Vector3 c2p2_world = _control_point_obj.transform.TransformPoint(circle2_point2);
            Debug.DrawLine(c1p1_world, c1p2_world, Color.white);
            Debug.DrawLine(c2p1_world, c2p2_world, Color.white);
            if (i % 10 == 0)
                Debug.DrawLine(c1p1_world, c2p1_world, new Color(0.5f, 0.5f, 0.5f));
        }
        #endregion FSP Before Loop

        #region FSP Loop
        // Perform linear search on the two 360s next to the initial guess.
        int prev_index_of_collided_cw = -1; // Initialize to -1 to indicate no collision.
        int prev_index_of_collided_ccw = -1; // Initialize to -1 to indicate no collision.
        Vector2 prev_ccw_valid_uv = new();
        Vector2 prev_cw_valid_uv = new();
        for (int i = 0; i <= _angular_resolution_per_rev; i++) {
            // Perform 1 CCW and 1 CW shift+test every outerloop, starting with CCW.
            for (int ccw_cw_enum = 0; ccw_cw_enum < 2; ccw_cw_enum++) {
                #region FSP Calculate Next UV
                // Define the theta shift in 3d space that we want.
                float theta_shift_wanted = 2 * Mathf.PI * i / _angular_resolution_per_rev;
                if (ccw_cw_enum == 1)
                    theta_shift_wanted = -theta_shift_wanted;

                // Calculate the uv angle shift that corresponds to a 3D angle shift of theta_shift.
                float theta_shift_uv = SurfaceAngleToUVAngle(
                    uv_dir_closest_surf_angle + theta_shift_wanted,
                    velo_u, velo_v)
                     - uv_dir_closest_uv_angle;

                // Calculate the shifted uv as uv_closest rotated by theta_shift.
                Vector2 uv_dir_shifted = new(
                    Mathf.Cos(theta_shift_uv) * uv_dir_closest.x - Mathf.Sin(theta_shift_uv) * uv_dir_closest.y,
                    Mathf.Sin(theta_shift_uv) * uv_dir_closest.x + Mathf.Cos(theta_shift_uv) * uv_dir_closest.y
                );

                // Renormalize in 3D space.
                // Rescale the uv direction vector by the calculation step size.
                // Do the same for the 3D vector.
                Vector3 uv_dir_3d =
                    uv_dir_shifted.x * _control_point_obj.CalcBSurfaceVelocityU(curr_uv.x, curr_uv.y) +
                    uv_dir_shifted.y * _control_point_obj.CalcBSurfaceVelocityV(curr_uv.x, curr_uv.y);
                uv_dir_shifted /= uv_dir_3d.magnitude;
                uv_dir_shifted *= _calculation_step_size;
                uv_dir_3d = _calculation_step_size * uv_dir_3d.normalized;

                // Calculate the next position in uv space and 3D space.
                Vector2 next_uv = curr_uv + uv_dir_shifted;
                Vector3 next_pos = curr_pos + uv_dir_3d;
                Vector3 next_world = _control_point_obj.transform.TransformPoint(next_pos);

                // Draw the tested line in the scene view for debugging.
                if (i == 0)
                    Debug.DrawLine(curr_world, Vector3.LerpUnclamped(curr_world, next_world, 1f), Color.cyan);
                #endregion FSP Calculate Next UV

                #region FSP Check Validity
                // Check if next_uv is valid.
                bool is_valid = true;


                // Check if it is too close to any other point in _uv_points[_testworthy_indices].
                if (PosCollidesWithStepover(next_pos, out int curr_index_of_collided)) {
                    is_valid = false;
                    Debug.DrawLine(curr_world, Vector3.LerpUnclamped(curr_world, next_world, 0.9f), Color.grey);
                }

                // Check if next_uv is inside the circle inscribing the BSurface.
                if (Vector2.Distance(new(0.5f, 0.5f), next_uv) > 0.5f) {
                    is_valid = false;
                    curr_index_of_collided = -2; // Set the collision index to -2: collision with outer boundary.
                    Debug.DrawLine(curr_world, Vector3.LerpUnclamped(curr_world, next_world, 0.8f), Color.red);
                }

                // Check if the turn is more than max_turning_radians.
                if (_uv_points.Count >= 2) {
                    Vector3 angle_vec1 = (-curr_pos + next_pos).normalized;
                    Vector3 prev_pos = _control_point_obj.CalcBsurface(_uv_points[^2].x, _uv_points[^2].y); // ^2 is the point before curr.
                    Vector3 angle_vec2 = (-prev_pos + curr_pos).normalized;
                    float angle = Mathf.Deg2Rad * Vector3.Angle(angle_vec1, angle_vec2);
                    if (angle > max_turning_radians) {
                        is_valid = false;
                        Debug.DrawLine(curr_world, Vector3.LerpUnclamped(curr_world, next_world, 0.6f), Color.purple);
                    }
                }

                // Check if the current solution matches the requested solution types.
                byte curr_solution_type = 0;
                // Set winding direction bits.
                if (i == 0) // First loop means the solution is directionless.
                    curr_solution_type = 0b0000;
                else if (ccw_cw_enum == 0)
                    if (sticky_mode == false) // True if we are currently in invalid space.
                        curr_solution_type |= 0b0001; // ccw_enum==0 represents CW solution in this case.
                    else // Runs if we are in valid space.
                        curr_solution_type |= 0b0010; // ccw_enum==0 represents a CCW solution in this case.
                else if (ccw_cw_enum == 1)
                    if (sticky_mode == false) // Ditto.
                        curr_solution_type |= 0b0010; // Vise versa.
                    else
                        curr_solution_type |= 0b0001; // Vise versa.
                                                      // Set based on closer-ness.
                if (Vector3.Distance(next_world, target_world) < Vector3.Distance(curr_world, target_world))
                    curr_solution_type |= 0b0100; // Closer.
                else
                    curr_solution_type |= 0b1000; // Farther.
                                                  // If every set flag in curr_solution_type is not also set in solution_requested, the point is invalid.
                if ((curr_solution_type & solution_requested) != curr_solution_type) {
                    is_valid = false;
                    Debug.DrawLine(curr_world, Vector3.LerpUnclamped(curr_world, next_world, 0.4f), Color.orange);
                }
                #endregion FSP Check Validity

                #region FSP Return Logic
                // In sticky mode, we only want to return the point if it is next to an invalid point.
                // If the initial guess is invalid, we want to turn sticky mode off, because the next valid point is gurenteed to be a sticky one.
                if (i == 0 && is_valid == false)
                    sticky_mode = false; // This bool will never be turned on after this.

                // Handle sticky mode logic.
                // If sticky mode is false, we want to return the first valid point we find.
                if (sticky_mode == false) {
                    if (is_valid == true) {
                        // Draw a debug line from the current point to the next point in the "shifted" direction.
                        Debug.DrawLine(curr_world, Vector3.LerpUnclamped(curr_world, next_world, 0.3f), Color.magenta);
                        solution_returned = curr_solution_type;
                        index_of_collided = (ccw_cw_enum == 0) ? prev_index_of_collided_ccw : prev_index_of_collided_cw;
                        return next_uv;
                    }
                    if (ccw_cw_enum == 0)
                        prev_index_of_collided_ccw = curr_index_of_collided;
                    else
                        prev_index_of_collided_cw = curr_index_of_collided;
                }
                else {
                    // We need to find the next invalid point, which is also in the direction we requested,
                    // then return the previous valid point in that direction.
                    bool curr_direction_is_valid = cw_is_valid && (ccw_cw_enum == 1) ||
                                                  ccw_is_valid && (ccw_cw_enum == 0);
                    if (is_valid == false && curr_direction_is_valid) {
                        // Impossible for prevs to be uninitialized as i > 0 is guaranteed.
                        Vector2 output_uv = (ccw_cw_enum == 0) ? prev_ccw_valid_uv : prev_cw_valid_uv;
                        Vector3 output_pos = _control_point_obj.CalcBsurface(output_uv.x, output_uv.y);
                        Vector3 output_world = _control_point_obj.transform.TransformPoint(output_pos);
                        Debug.DrawLine(curr_world, Vector3.LerpUnclamped(curr_world, output_world, 0.3f), Color.magenta);

                        solution_returned = curr_solution_type;
                        index_of_collided = curr_index_of_collided;
                        return output_uv;
                    }
                    // Update the corresponding previous valid point.
                    if (ccw_cw_enum == 0)
                        prev_ccw_valid_uv = next_uv;
                    else
                        prev_cw_valid_uv = next_uv;
                }
                #endregion FSP Handle Validity
            }
        }
        #endregion FSP Loop

        // If nothing was returned, return NaN.
        solution_returned = 0;
        index_of_collided = -1; // No collision.
        return new Vector2(float.NaN, float.NaN);
    }
    #endregion FindStepoverPoint (FSP)

    /// <summary>
    /// Checks if the next position is valid by checking if it is at least _stepover distance away from every other point in _uv_points.
    /// </summary>
    bool PosCollidesWithStepover(in Vector3 next_pos, out int index_of_collided) {
        for (int i_uv = 0; i_uv < _testworthy_indices.Count; i_uv++) {
            if (_testworthy_indices[i_uv] > _uv_points.Count - 1) {
                _testworthy_indices.RemoveAt(i_uv);
                continue;
            }
            Vector2 other_uv = _uv_points[_testworthy_indices[i_uv]];
            // Calculate the 3D position of the other uv point.
            Vector3 other_pos = _control_point_obj.CalcBsurface(other_uv.x, other_uv.y);
            // Calculate the distance between the next point and the other point.
            float distance = Vector3.Distance(next_pos, other_pos);
            // If the distance is less than the stepover distance, the point is not valid.
            if (distance < _stepover) {
                index_of_collided = _testworthy_indices[i_uv];
                return true;
            }
        }
        index_of_collided = -1; // No collision.
        return false;
    }

    /// <summary>
    /// Converts an angle in 3D space, relative to velo_u on the surface tangent plane, to an angle in uv space.
    /// </summary>
    float SurfaceAngleToUVAngle(float surface_angle, Vector3 velo_u, Vector3 velo_v) {
        // Turn the 3D velocity vectors into their 2D tangent plane counterparts (x axis alligned with velo_u).
        Vector2 velo_u_tangent = new(velo_u.magnitude, 0);
        float angle_between_velo_uv = Vector3.SignedAngle(velo_u, velo_v, Vector3.Cross(velo_u, velo_v));
        // Convert from degrees to radians (took an hour to figure out).
        Vector2 velo_v_tangent = new(
            velo_v.magnitude * Mathf.Cos(angle_between_velo_uv * Mathf.Deg2Rad),
            velo_v.magnitude * Mathf.Sin(angle_between_velo_uv * Mathf.Deg2Rad)
        );

        // Perform arithmetic.
        // Find the tan2 of the requested angle.
        float2 tan2_surface = new(Mathf.Cos(surface_angle), Mathf.Sin(surface_angle));
        // Apply inverse transformation (the one that takes u&v's velo_tangents to unit vectors).
        // Transformation matrix takes the form, [ vy -vx ; -uy ux ] / ux*vy-uy*vx.
        float2 tan2_uv = new(
            tan2_surface.x * velo_v_tangent.y - tan2_surface.y * velo_v_tangent.x,
            -tan2_surface.x * velo_u_tangent.y + tan2_surface.y * velo_u_tangent.x
        );

        // We dont need to divide by the determinant because it doesnt change the angle.
        // Undo tan2 to get the angle in uv space.
        return Mathf.Atan2(tan2_uv.y, tan2_uv.x);
    }
}
