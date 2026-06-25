using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Exports CSV data for Paper 1 experiments and future use, organized by measurement type.
/// Attach to any manager GameObject. Assign references in the Inspector.
/// All files are written to Application.persistentDataPath and the path is logged to the Console.
///
/// Exports:
///   Loop Points                  — raw 3D positions of the drawn loop
///   Loop Projection Displacement — per loop point, how far surface projection moved it
///   Toolpath Points              — UV and world positions of every toolpath point
///   Toolpath Arc Length          — cumulative 3D arc length at every toolpath point
///   Surface Normals              — normal, du, dv vectors at every toolpath point
///   B-Spline Control Points      — full control point grid (reconstructs the fitted surface)
///   Surface Area                 — estimated area of the B-surface patch in m² and mm²
///   Fit Error                    — per loop point, distance to nearest B-surface point + RMSE
///   Stepover Distances           — per toolpath point, distance to nearest non-adjacent pass
///   Coverage                     — per UV surface sample, covered/uncovered + nearest distance
/// </summary>
public class ExperimentDataExporter : MonoBehaviour
{
    [Header("Required references")]
    public BsplineManager _bspline;
    public SurfaceFittingManager _fitting_manager;
    public BSurfaceGcodeGenerator _gcode_generator;

    [Header("Fit error settings")]
    [Tooltip("UV sample grid resolution for finding the nearest surface point. Higher = more accurate but slower.")]
    public int _fit_surface_sample_resolution = 50;

    [Header("Coverage settings")]
    [Tooltip("UV sample grid resolution for the coverage check.")]
    public int _coverage_sample_resolution = 100;
    [Tooltip("A surface sample is covered if any toolpath point is within (_stepover * this fraction).")]
    [Range(0.1f, 1.0f)]
    public float _coverage_radius_fraction = 0.5f;

    [Header("Surface area settings")]
    [Tooltip("UV sample grid resolution for surface area estimation. Higher = more accurate.")]
    public int _surface_area_sample_resolution = 100;

    // -------------------------------------------------------------------------
    // Export: Loop Points
    // Raw 3D world-space positions of every point in the drawn loop.
    // Use this to inspect or re-analyze the surgeon's sketch offline.
    // -------------------------------------------------------------------------

    [ContextMenu("Export Loop Points")]
    public void ExportLoopPoints()
    {
        if (!ValidateReferences()) return;

        LineRenderer loop = _fitting_manager._drawn_loop;
        int n = loop.positionCount;
        if (n < 2)
        {
            Debug.LogError("ExperimentDataExporter: drawn loop has fewer than 2 points.");
            return;
        }

        List<string> rows = new List<string>();
        rows.Add("point_index,world_x,world_y,world_z");

        for (int i = 0; i < n; i++)
        {
            Vector3 p = loop.GetPosition(i);
            rows.Add($"{i},{p.x:F6},{p.y:F6},{p.z:F6}");
        }

        rows.Add("");
        rows.Add($"total_points,{n},,");

        WriteCSV("LoopPoints", rows);
        Debug.Log($"ExperimentDataExporter (LoopPoints): exported {n} points.");
    }

    // -------------------------------------------------------------------------
    // Export: Loop Projection Displacement
    // Records each loop point before and after surface projection, and the
    // 3D displacement magnitude. Only meaningful when _enable_surface_constraint
    // is true. Call this BEFORE fitting (it captures the pre-projection state
    // from a cached snapshot — see note below).
    //
    // Usage: call CachePreProjectionLoop() before fitting, then call this export
    // after fitting to get the displacement. Or call both together via
    // ExportLoopProjectionDisplacement() which does both steps automatically.
    // -------------------------------------------------------------------------

    Vector3[] _pre_projection_loop_cache = null;

    /// <summary>
    /// Snapshots the current loop positions before surface projection runs.
    /// Call this immediately before FitSurfaceToDrawingEllipticalHeuristic()
    /// or FitSurfaceToDrawingLeastSquares() if you want projection displacement data.
    /// </summary>
    public void CachePreProjectionLoop()
    {
        LineRenderer loop = _fitting_manager._drawn_loop;
        int n = loop.positionCount;
        _pre_projection_loop_cache = new Vector3[n];
        for (int i = 0; i < n; i++)
            _pre_projection_loop_cache[i] = loop.GetPosition(i);
        Debug.Log($"ExperimentDataExporter: cached {n} pre-projection loop points.");
    }

    [ContextMenu("Export Loop Projection Displacement")]
    public void ExportLoopProjectionDisplacement()
    {
        if (!ValidateReferences()) return;

        LineRenderer loop = _fitting_manager._drawn_loop;
        int n = loop.positionCount;

        if (_pre_projection_loop_cache == null)
        {
            Debug.LogError("ExperimentDataExporter: no pre-projection cache found. " +
                           "Call CachePreProjectionLoop() before fitting.");
            return;
        }
        if (_pre_projection_loop_cache.Length != n)
        {
            Debug.LogWarning("ExperimentDataExporter: cache length does not match current loop length. " +
                             "The loop may have been modified after caching.");
        }

        int compare_count = Mathf.Min(n, _pre_projection_loop_cache.Length);

        List<string> rows = new List<string>();
        rows.Add("point_index," +
                 "pre_x,pre_y,pre_z," +
                 "post_x,post_y,post_z," +
                 "displacement_m,displacement_mm");

        double sum_sq = 0.0;
        float max_disp = 0f;

        for (int i = 0; i < compare_count; i++)
        {
            Vector3 pre  = _pre_projection_loop_cache[i];
            Vector3 post = loop.GetPosition(i);
            float disp   = Vector3.Distance(pre, post);
            sum_sq += disp * disp;
            if (disp > max_disp) max_disp = disp;

            rows.Add($"{i}," +
                     $"{pre.x:F6},{pre.y:F6},{pre.z:F6}," +
                     $"{post.x:F6},{post.y:F6},{post.z:F6}," +
                     $"{disp:F6},{disp * 1000f:F4}");
        }

        double rmse_disp_mm = Math.Sqrt(sum_sq / compare_count) * 1000.0;

        rows.Add("");
        rows.Add("summary,,,,,,,,");
        rows.Add($"point_count,{compare_count},,,,,,,");
        rows.Add($"rmse_displacement_mm,{rmse_disp_mm:F4},,,,,,,");
        rows.Add($"max_displacement_mm,{max_disp * 1000f:F4},,,,,,,");

        WriteCSV("LoopProjectionDisplacement", rows);
        Debug.Log($"ExperimentDataExporter (LoopProjectionDisplacement): " +
                  $"RMSE displacement = {rmse_disp_mm:F4} mm | max = {max_disp * 1000f:F4} mm | n = {compare_count}");
    }

    // -------------------------------------------------------------------------
    // Export: Toolpath Points
    // UV coordinates and world-space positions of every toolpath point.
    // Use this to inspect path geometry or re-run analysis offline.
    // -------------------------------------------------------------------------

    [ContextMenu("Export Toolpath Points")]
    public void ExportToolpathPoints()
    {
        if (!ValidateReferences()) return;

        List<Vector2> uv_pts = _gcode_generator._uv_points;
        int n = uv_pts.Count;
        if (n < 2)
        {
            Debug.LogError("ExperimentDataExporter: toolpath has fewer than 2 points. Generate a toolpath first.");
            return;
        }

        List<string> rows = new List<string>();
        rows.Add("point_index,u,v,world_x,world_y,world_z");

        for (int i = 0; i < n; i++)
        {
            Vector3 world = _bspline.transform.TransformPoint(
                _bspline.CalcBsurface(uv_pts[i].x, uv_pts[i].y));
            rows.Add($"{i},{uv_pts[i].x:F6},{uv_pts[i].y:F6}," +
                     $"{world.x:F6},{world.y:F6},{world.z:F6}");
        }

        rows.Add("");
        rows.Add($"total_points,{n},,,,");

        WriteCSV("ToolpathPoints", rows);
        Debug.Log($"ExperimentDataExporter (ToolpathPoints): exported {n} points.");
    }

    // -------------------------------------------------------------------------
    // Export: Toolpath Arc Length
    // Cumulative 3D arc length at every toolpath point, starting from 0.
    // Useful for normalizing comparisons across surfaces of different sizes
    // and for feedrate / print-time estimation.
    // -------------------------------------------------------------------------

    [ContextMenu("Export Toolpath Arc Length")]
    public void ExportToolpathArcLength()
    {
        if (!ValidateReferences()) return;

        List<Vector2> uv_pts = _gcode_generator._uv_points;
        int n = uv_pts.Count;
        if (n < 2)
        {
            Debug.LogError("ExperimentDataExporter: toolpath has fewer than 2 points. Generate a toolpath first.");
            return;
        }

        // Cache world positions.
        Vector3[] positions = new Vector3[n];
        for (int i = 0; i < n; i++)
            positions[i] = _bspline.transform.TransformPoint(
                _bspline.CalcBsurface(uv_pts[i].x, uv_pts[i].y));

        List<string> rows = new List<string>();
        rows.Add("point_index,u,v,world_x,world_y,world_z,segment_length_m,cumulative_arc_length_m");

        float cumulative = 0f;
        rows.Add($"0,{uv_pts[0].x:F6},{uv_pts[0].y:F6}," +
                 $"{positions[0].x:F6},{positions[0].y:F6},{positions[0].z:F6}," +
                 $"0,0");

        for (int i = 1; i < n; i++)
        {
            float seg = Vector3.Distance(positions[i], positions[i - 1]);
            cumulative += seg;
            rows.Add($"{i},{uv_pts[i].x:F6},{uv_pts[i].y:F6}," +
                     $"{positions[i].x:F6},{positions[i].y:F6},{positions[i].z:F6}," +
                     $"{seg:F6},{cumulative:F6}");
        }

        rows.Add("");
        rows.Add("summary,,,,,,, ");
        rows.Add($"total_points,{n},,,,,, ");
        rows.Add($"total_arc_length_m,{cumulative:F6},,,,,, ");
        rows.Add($"total_arc_length_mm,{cumulative * 1000f:F4},,,,,, ");

        WriteCSV("ToolpathArcLength", rows);
        Debug.Log($"ExperimentDataExporter (ToolpathArcLength): total = {cumulative * 1000f:F4} mm | n = {n}");
    }

    // -------------------------------------------------------------------------
    // Export: Surface Normals
    // Normal vector, du tangent, and dv tangent at every toolpath point.
    // Required for Paper 2 robot orientation validation (dVRK tool axis alignment).
    // -------------------------------------------------------------------------

    [ContextMenu("Export Surface Normals")]
    public void ExportSurfaceNormals()
    {
        if (!ValidateReferences()) return;

        List<Vector2> uv_pts = _gcode_generator._uv_points;
        int n = uv_pts.Count;
        if (n < 2)
        {
            Debug.LogError("ExperimentDataExporter: toolpath has fewer than 2 points. Generate a toolpath first.");
            return;
        }

        List<string> rows = new List<string>();
        rows.Add("point_index,u,v,world_x,world_y,world_z," +
                 "normal_x,normal_y,normal_z," +
                 "du_x,du_y,du_z," +
                 "dv_x,dv_y,dv_z");

        for (int i = 0; i < n; i++)
        {
            float u = uv_pts[i].x;
            float v = uv_pts[i].y;

            Vector3 pos_local = _bspline.CalcBsurface(u, v);
            Vector3 du_local  = _bspline.CalcBSurfaceVelocityU(u, v);
            Vector3 dv_local  = _bspline.CalcBSurfaceVelocityV(u, v);

            // Normal is cross(du, dv), negated to match Unity LHR convention
            // (same sign convention used in BSurfaceGcodeGenerator).
            Vector3 normal_local = -Vector3.Cross(du_local, dv_local).normalized;

            // Transform all vectors to world space.
            Vector3 pos_world    = _bspline.transform.TransformPoint(pos_local);
            Vector3 normal_world = _bspline.transform.TransformDirection(normal_local).normalized;
            Vector3 du_world     = _bspline.transform.TransformDirection(du_local).normalized;
            Vector3 dv_world     = _bspline.transform.TransformDirection(dv_local).normalized;

            rows.Add($"{i},{u:F6},{v:F6}," +
                     $"{pos_world.x:F6},{pos_world.y:F6},{pos_world.z:F6}," +
                     $"{normal_world.x:F6},{normal_world.y:F6},{normal_world.z:F6}," +
                     $"{du_world.x:F6},{du_world.y:F6},{du_world.z:F6}," +
                     $"{dv_world.x:F6},{dv_world.y:F6},{dv_world.z:F6}");
        }

        rows.Add("");
        rows.Add($"total_points,{n}");

        WriteCSV("SurfaceNormals", rows);
        Debug.Log($"ExperimentDataExporter (SurfaceNormals): exported {n} points.");
    }

    // -------------------------------------------------------------------------
    // Export: B-Spline Control Points
    // The full control point grid that defines the fitted B-surface.
    // Saving this lets you reconstruct the surface exactly offline without
    // needing to re-run the fit.
    // -------------------------------------------------------------------------

    [ContextMenu("Export B-Spline Control Points")]
    public void ExportBSplineControlPoints()
    {
        if (!ValidateReferences()) return;

        if (_bspline._control_points == null)
        {
            Debug.LogError("ExperimentDataExporter: control points are null.");
            return;
        }

        int size_u = _bspline._control_points.GetLength(0);
        int size_v = _bspline._control_points.GetLength(1);

        List<string> rows = new List<string>();
        rows.Add("index_u,index_v,local_x,local_y,local_z,world_x,world_y,world_z");

        for (int u = 0; u < size_u; u++)
            for (int v = 0; v < size_v; v++)
            {
                Vector3 local = _bspline._control_points[u, v];
                Vector3 world = _bspline.transform.TransformPoint(local);
                rows.Add($"{u},{v}," +
                         $"{local.x:F6},{local.y:F6},{local.z:F6}," +
                         $"{world.x:F6},{world.y:F6},{world.z:F6}");
            }

        rows.Add("");
        rows.Add($"grid_size_u,{size_u},,,,,,");
        rows.Add($"grid_size_v,{size_v},,,,,,");
        rows.Add($"surface_degree,3,,,,,,");

        // Also record the knot vector.
        rows.Add("");
        rows.Add("knot_vector_index,knot_value,,,,,,");
        for (int k = 0; k < _bspline._knot_vector.Length; k++)
            rows.Add($"{k},{_bspline._knot_vector[k]:F6},,,,,,");

        WriteCSV("BSplineControlPoints", rows);
        Debug.Log($"ExperimentDataExporter (BSplineControlPoints): exported {size_u}x{size_v} grid.");
    }

    // -------------------------------------------------------------------------
    // Export: Surface Area
    // Estimates the area of the B-surface patch by summing triangle areas
    // across a UV sample grid. Needed to normalize coverage by actual surface
    // area rather than UV sample count.
    // -------------------------------------------------------------------------

    [ContextMenu("Export Surface Area")]
    public void ExportSurfaceArea()
    {
        if (!ValidateReferences()) return;

        int res = _surface_area_sample_resolution;
        float step = 1f / (res - 1);

        double total_area_m2 = 0.0;
        int quad_count = 0;

        List<string> rows = new List<string>();
        rows.Add("quad_index,u0,v0,quad_area_m2,quad_area_mm2");

        // Iterate over UV quads inside the unit circle.
        // Each quad is split into 2 triangles; area = 0.5 * |cross(AB, AC)|.
        int quad_idx = 0;
        for (int ui = 0; ui < res - 1; ui++)
            for (int vi = 0; vi < res - 1; vi++)
            {
                float u0 = ui * step;
                float v0 = vi * step;
                float u1 = u0 + step;
                float v1 = v0 + step;

                // Skip quads whose center falls outside the unit circle.
                Vector2 center_uv = new Vector2((u0 + u1) * 0.5f, (v0 + v1) * 0.5f);
                if (Vector2.Distance(center_uv, new Vector2(0.5f, 0.5f)) > 0.5f)
                    continue;

                Vector3 p00 = _bspline.transform.TransformPoint(_bspline.CalcBsurface(u0, v0));
                Vector3 p10 = _bspline.transform.TransformPoint(_bspline.CalcBsurface(u1, v0));
                Vector3 p01 = _bspline.transform.TransformPoint(_bspline.CalcBsurface(u0, v1));
                Vector3 p11 = _bspline.transform.TransformPoint(_bspline.CalcBsurface(u1, v1));

                // Two triangles: (p00, p10, p01) and (p10, p11, p01).
                float area_tri1 = Vector3.Cross(p10 - p00, p01 - p00).magnitude * 0.5f;
                float area_tri2 = Vector3.Cross(p11 - p10, p01 - p10).magnitude * 0.5f;
                float quad_area = area_tri1 + area_tri2;

                total_area_m2 += quad_area;
                quad_count++;

                rows.Add($"{quad_idx},{u0:F4},{v0:F4},{quad_area:F8},{quad_area * 1e6:F4}");
                quad_idx++;
            }

        double total_area_mm2 = total_area_m2 * 1e6;
        double total_area_cm2 = total_area_m2 * 1e4;

        rows.Add("");
        rows.Add("summary,,,, ");
        rows.Add($"sample_resolution,{res}x{res},,,");
        rows.Add($"quad_count,{quad_count},,,");
        rows.Add($"total_area_m2,{total_area_m2:F8},,,");
        rows.Add($"total_area_mm2,{total_area_mm2:F4},,,");
        rows.Add($"total_area_cm2,{total_area_cm2:F4},,,");

        WriteCSV("SurfaceArea", rows);
        Debug.Log($"ExperimentDataExporter (SurfaceArea): {total_area_cm2:F4} cm² ({total_area_mm2:F4} mm²) | {quad_count} quads");
    }

    // -------------------------------------------------------------------------
    // Export: Fit Error
    // For each drawn loop point, finds the nearest point on the B-surface
    // (by sampling a UV grid) and records the 3D Euclidean distance in meters.
    // Call after either fitting method — the timestamp in the filename
    // distinguishes elliptical vs. least-squares runs.
    // Covers Exp 1 (elliptical) and Exp 2 (least-squares).
    // -------------------------------------------------------------------------

    [ContextMenu("Export Fit Error")]
    public void ExportFitError()
    {
        if (!ValidateReferences()) return;

        LineRenderer loop = _fitting_manager._drawn_loop;
        int loop_count = loop.positionCount;
        if (loop_count < 3)
        {
            Debug.LogError("ExperimentDataExporter: drawn loop has fewer than 3 points. Fit a surface first.");
            return;
        }

        // Pre-sample the B-surface on a UV grid (inside unit circle only).
        int res = _fit_surface_sample_resolution;
        List<Vector3> surface_samples = new List<Vector3>(res * res);
        for (int ui = 0; ui < res; ui++)
            for (int vi = 0; vi < res; vi++)
            {
                float u = (float)ui / (res - 1);
                float v = (float)vi / (res - 1);
                if (Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) > 0.5f)
                    continue;
                surface_samples.Add(_bspline.transform.TransformPoint(_bspline.CalcBsurface(u, v)));
            }

        List<string> rows = new List<string>();
        rows.Add("point_index,loop_x,loop_y,loop_z," +
                 "nearest_surface_x,nearest_surface_y,nearest_surface_z," +
                 "distance_m,distance_mm");

        double sum_sq = 0.0;
        float max_err = 0f;
        float min_err = float.MaxValue;

        for (int i = 0; i < loop_count; i++)
        {
            Vector3 loop_pt   = loop.GetPosition(i);
            float best_dist   = float.MaxValue;
            Vector3 best_pt   = Vector3.zero;

            foreach (Vector3 sp in surface_samples)
            {
                float d = Vector3.Distance(loop_pt, sp);
                if (d < best_dist) { best_dist = d; best_pt = sp; }
            }

            sum_sq += best_dist * best_dist;
            if (best_dist > max_err) max_err = best_dist;
            if (best_dist < min_err) min_err = best_dist;

            rows.Add($"{i},{loop_pt.x:F6},{loop_pt.y:F6},{loop_pt.z:F6}," +
                     $"{best_pt.x:F6},{best_pt.y:F6},{best_pt.z:F6}," +
                     $"{best_dist:F6},{best_dist * 1000f:F4}");
        }

        double rmse_m  = Math.Sqrt(sum_sq / loop_count);
        double rmse_mm = rmse_m * 1000.0;

        rows.Add("");
        rows.Add("summary,,,,,,,,");
        rows.Add($"loop_point_count,{loop_count},,,,,,,");
        rows.Add($"surface_sample_count,{surface_samples.Count},,,,,,,");
        rows.Add($"rmse_m,{rmse_m:F6},,,,,,,");
        rows.Add($"rmse_mm,{rmse_mm:F4},,,,,,,");
        rows.Add($"max_error_mm,{max_err * 1000f:F4},,,,,,,");
        rows.Add($"min_error_mm,{min_err * 1000f:F4},,,,,,,");

        WriteCSV("FitError", rows);
        Debug.Log($"ExperimentDataExporter (FitError): RMSE = {rmse_mm:F4} mm | max = {max_err * 1000f:F4} mm | n = {loop_count}");
    }

    // -------------------------------------------------------------------------
    // Export: Stepover Distances
    // For each toolpath point, finds the nearest point from a non-adjacent pass
    // and records the 3D distance. Mirrors the logic in
    // ConformalToolpathingManager.StepoverAtPoint.
    // Covers Exp 3 (stepover uniformity).
    // -------------------------------------------------------------------------

    [ContextMenu("Export Stepover Distances")]
    public void ExportStepoverDistances()
    {
        if (!ValidateReferences()) return;

        List<Vector2> uv_pts = _gcode_generator._uv_points;
        int n = uv_pts.Count;
        if (n < 10)
        {
            Debug.LogError("ExperimentDataExporter: toolpath has fewer than 10 points. Generate a toolpath first.");
            return;
        }

        // Cache world positions.
        Vector3[] positions = new Vector3[n];
        for (int i = 0; i < n; i++)
            positions[i] = _bspline.transform.TransformPoint(
                _bspline.CalcBsurface(uv_pts[i].x, uv_pts[i].y));

        float target_stepover = _gcode_generator._stepover;

        List<string> rows = new List<string>();
        rows.Add("point_index,u,v,world_x,world_y,world_z," +
                 "nearest_nonadj_index,distance_m,distance_mm,deviation_from_target_mm");

        List<float> valid_distances = new List<float>();

        for (int i = 0; i < n; i++)
        {
            int nearest_idx = FindNearestNonAdjacentIndex(positions, i);
            if (nearest_idx < 0)
            {
                rows.Add($"{i},{uv_pts[i].x:F6},{uv_pts[i].y:F6}," +
                         $"{positions[i].x:F6},{positions[i].y:F6},{positions[i].z:F6}," +
                         $"-1,,,");
                continue;
            }

            float dist   = Vector3.Distance(positions[i], positions[nearest_idx]);
            float dev_mm = (dist - target_stepover) * 1000f;
            valid_distances.Add(dist);

            rows.Add($"{i},{uv_pts[i].x:F6},{uv_pts[i].y:F6}," +
                     $"{positions[i].x:F6},{positions[i].y:F6},{positions[i].z:F6}," +
                     $"{nearest_idx},{dist:F6},{dist * 1000f:F4},{dev_mm:F4}");
        }

        int valid_count = valid_distances.Count;
        double mean = 0.0;
        float max_d = 0f, min_d = float.MaxValue;
        foreach (float d in valid_distances)
        {
            mean += d;
            if (d > max_d) max_d = d;
            if (d < min_d) min_d = d;
        }
        mean /= valid_count;

        double variance = 0.0;
        foreach (float d in valid_distances)
            variance += (d - mean) * (d - mean);
        double std_dev = Math.Sqrt(variance / valid_count);
        double cv = (mean > 0) ? std_dev / mean : 0.0;

        rows.Add("");
        rows.Add("summary,,,,,,,,,");
        rows.Add($"toolpath_point_count,{n},,,,,,,,");
        rows.Add($"valid_measurements,{valid_count},,,,,,,,");
        rows.Add($"target_stepover_mm,{target_stepover * 1000f:F4},,,,,,,,");
        rows.Add($"mean_stepover_mm,{mean * 1000.0:F4},,,,,,,,");
        rows.Add($"std_dev_mm,{std_dev * 1000.0:F4},,,,,,,,");
        rows.Add($"max_stepover_mm,{max_d * 1000f:F4},,,,,,,,");
        rows.Add($"min_stepover_mm,{min_d * 1000f:F4},,,,,,,,");
        rows.Add($"coefficient_of_variation,{cv:F4},,,,,,,,");

        WriteCSV("StepoverDistances", rows);
        Debug.Log($"ExperimentDataExporter (StepoverDistances): mean = {mean * 1000.0:F4} mm | " +
                  $"std = {std_dev * 1000.0:F4} mm | CV = {cv:F4} | n = {valid_count}");
    }

    // -------------------------------------------------------------------------
    // Export: Coverage
    // Samples the B-surface on a UV grid. Each sample is marked covered (1)
    // or uncovered (0) based on whether any toolpath point falls within
    // (_stepover * _coverage_radius_fraction) of it in 3D space.
    // Covers Exp 4 (coverage completeness).
    // -------------------------------------------------------------------------

    [ContextMenu("Export Coverage")]
    public void ExportCoverage()
    {
        if (!ValidateReferences()) return;

        List<Vector2> uv_pts = _gcode_generator._uv_points;
        if (uv_pts.Count < 2)
        {
            Debug.LogError("ExperimentDataExporter: toolpath has fewer than 2 points. Generate a toolpath first.");
            return;
        }

        // Cache toolpath world positions.
        Vector3[] toolpath_positions = new Vector3[uv_pts.Count];
        for (int i = 0; i < uv_pts.Count; i++)
            toolpath_positions[i] = _bspline.transform.TransformPoint(
                _bspline.CalcBsurface(uv_pts[i].x, uv_pts[i].y));

        float coverage_radius = _gcode_generator._stepover * _coverage_radius_fraction;
        int res = _coverage_sample_resolution;

        List<string> rows = new List<string>();
        rows.Add("sample_index,u,v,world_x,world_y,world_z,covered,nearest_toolpath_dist_m,nearest_toolpath_dist_mm");

        int total = 0, covered_count = 0;

        for (int ui = 0; ui < res; ui++)
            for (int vi = 0; vi < res; vi++)
            {
                float u = (float)ui / (res - 1);
                float v = (float)vi / (res - 1);
                if (Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) > 0.5f)
                    continue;

                Vector3 surface_pt = _bspline.transform.TransformPoint(_bspline.CalcBsurface(u, v));

                float nearest = float.MaxValue;
                foreach (Vector3 tp in toolpath_positions)
                {
                    float d = Vector3.Distance(surface_pt, tp);
                    if (d < nearest) nearest = d;
                }

                bool covered = nearest <= coverage_radius;
                if (covered) covered_count++;
                total++;

                rows.Add($"{total - 1},{u:F4},{v:F4}," +
                         $"{surface_pt.x:F6},{surface_pt.y:F6},{surface_pt.z:F6}," +
                         $"{(covered ? 1 : 0)},{nearest:F6},{nearest * 1000f:F4}");
            }

        float coverage_pct = total > 0 ? 100f * covered_count / total : 0f;

        rows.Add("");
        rows.Add("summary,,,,,,,,");
        rows.Add($"sample_resolution,{res}x{res},,,,,,,");
        rows.Add($"total_surface_samples,{total},,,,,,,");
        rows.Add($"covered_samples,{covered_count},,,,,,,");
        rows.Add($"coverage_percent,{coverage_pct:F2},,,,,,,");
        rows.Add($"coverage_radius_m,{coverage_radius:F6},,,,,,,");
        rows.Add($"target_stepover_m,{_gcode_generator._stepover:F6},,,,,,,");

        WriteCSV("Coverage", rows);
        Debug.Log($"ExperimentDataExporter (Coverage): {coverage_pct:F2}% ({covered_count}/{total} samples) | " +
                  $"radius = {coverage_radius * 1000f:F4} mm");
    }

    // -------------------------------------------------------------------------
    // Shared helpers
    // -------------------------------------------------------------------------

    // Mirrors ConformalToolpathingManager.StepoverAtPoint.
    // Returns the index of the nearest toolpath point not in the immediately
    // adjacent run of increasing distance from index.
    int FindNearestNonAdjacentIndex(Vector3[] positions, int index)
    {
        int n = positions.Length;

        // Backwards.
        int nearest_back = -1;
        float nearest_back_dist = float.MaxValue;
        {
            float prev_dist = -1f;
            int search_start = -1;
            for (int i = index; i >= 0; i--)
            {
                float d = Vector3.Distance(positions[index], positions[i]);
                if (prev_dist >= 0 && d < prev_dist) { search_start = i; break; }
                prev_dist = d;
            }
            if (search_start >= 0)
                for (int i = search_start; i >= 0; i--)
                {
                    float d = Vector3.Distance(positions[index], positions[i]);
                    if (d < nearest_back_dist) { nearest_back_dist = d; nearest_back = i; }
                }
        }

        // Forwards.
        int nearest_fwd = -1;
        float nearest_fwd_dist = float.MaxValue;
        {
            float prev_dist = -1f;
            int search_start = n;
            for (int i = index; i < n; i++)
            {
                float d = Vector3.Distance(positions[index], positions[i]);
                if (prev_dist >= 0 && d < prev_dist) { search_start = i; break; }
                prev_dist = d;
            }
            if (search_start < n)
                for (int i = search_start; i < n; i++)
                {
                    float d = Vector3.Distance(positions[index], positions[i]);
                    if (d < nearest_fwd_dist) { nearest_fwd_dist = d; nearest_fwd = i; }
                }
        }

        if (nearest_back < 0 && nearest_fwd < 0) return -1;
        if (nearest_back < 0) return nearest_fwd;
        if (nearest_fwd < 0) return nearest_back;
        return (nearest_fwd_dist <= nearest_back_dist) ? nearest_fwd : nearest_back;
    }

    bool ValidateReferences()
    {
        if (_bspline == null)
        {
            Debug.LogError("ExperimentDataExporter: _bspline is not assigned.");
            return false;
        }
        if (_fitting_manager == null)
        {
            Debug.LogError("ExperimentDataExporter: _fitting_manager is not assigned.");
            return false;
        }
        if (_gcode_generator == null)
        {
            Debug.LogError("ExperimentDataExporter: _gcode_generator is not assigned.");
            return false;
        }
        if (_bspline._control_points == null)
        {
            Debug.LogError("ExperimentDataExporter: B-spline surface is not defined yet.");
            return false;
        }
        return true;
    }

    void WriteCSV(string label, List<string> rows)
    {
        string timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
        string filename  = $"{label}_{timestamp}.csv";
        string path      = Path.Combine(Application.persistentDataPath, filename);
        try
        {
            File.WriteAllLines(path, rows);
            Debug.Log($"ExperimentDataExporter: saved → {path}");
        }
        catch (Exception ex)
        {
            Debug.LogError($"ExperimentDataExporter: failed to write {filename}: {ex.Message}");
        }
    }
}
