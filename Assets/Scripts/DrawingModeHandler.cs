using UnityEngine;

public class DrawingModeHandler : MonoBehaviour
{
    bool _drawing_mode_enabled;
    public GameObject _drawing_ui;
    public GameObject _drawing_cursor;
    public GameObject _projected_cursor;          // NEW: second dot snapped to anatomy surface
    public OVRCameraRig _tracking_space;
    public LineRenderer _line_renderer;

    [Header("Surface Projection")]
    public ClinicalScenarioManager _scenario_manager;   // NEW
    public LayerMask _anatomy_layer_mask;               // NEW
    public float _projection_offset = 0.5f;             // NEW: how far above mesh to start ray
    public float _projection_max_distance = 5.0f;       // NEW

    void Awake()
    {
        _drawing_mode_enabled = false;
        if (_drawing_ui == null)
            Debug.LogError("Drawing UI isnt set in DrawingModeHandler.");
        if (_tracking_space == null)
            Debug.LogError("Tracking space isnt set in DrawingModeHandler.");
        if (_line_renderer == null)
            Debug.LogError("Line renderer isnt set in DrawingModeHandler.");
    }

    void Update()
    {
        // Move floating cursor to controller tip.
        Vector3 controller_pos = _tracking_space.rightControllerAnchor.TransformPoint(
            _tracking_space.rightControllerAnchor.localPosition + Vector3.forward * 0.1f);
        _drawing_cursor.transform.position = controller_pos;

        // Project cursor onto anatomy surface and move projected dot.
        UpdateProjectedCursor(controller_pos);
    }

    void UpdateProjectedCursor(Vector3 controller_pos)
    {
        if (_projected_cursor == null || _scenario_manager == null) return;

        // Compute ray start: above the anatomy bounding box at the cursor's XZ.
        float ray_start_y = _scenario_manager.GetAnatomyCentroid().y + _projection_offset;
        GameObject anatomy = _scenario_manager.GetActiveAnatomy();
        if (anatomy != null)
        {
            MeshFilter mf = anatomy.GetComponent<MeshFilter>();
            if (mf != null && mf.mesh != null)
            {
                Vector3 world_max = anatomy.transform.TransformPoint(mf.mesh.bounds.max);
                ray_start_y = world_max.y + _projection_offset;
            }
        }

        Vector3 ray_origin = new Vector3(controller_pos.x, ray_start_y, controller_pos.z);

        bool prev_backfaces = Physics.queriesHitBackfaces;
        Physics.queriesHitBackfaces = true;
        RaycastHit[] hits = Physics.RaycastAll(ray_origin, Vector3.down, _projection_max_distance, _anatomy_layer_mask);
        Physics.queriesHitBackfaces = prev_backfaces;

        // Pick the highest-Y hit (top surface).
        float best_y = float.NegativeInfinity;
        Vector3 best_point = Vector3.zero;
        bool found = false;
        foreach (RaycastHit h in hits)
        {
            if (h.point.y > best_y) { best_y = h.point.y; best_point = h.point; found = true; }
        }

        // Fallback: shoot inward toward anatomy centroid.
        if (!found)
        {
            Vector3 centroid = _scenario_manager.GetAnatomyCentroid();
            Vector3 inward = (centroid - controller_pos).normalized;
            Vector3 fallback_origin = controller_pos - inward * _projection_offset;
            if (Physics.Raycast(fallback_origin, inward, out RaycastHit fallback_hit, _projection_max_distance, _anatomy_layer_mask))
            {
                best_point = fallback_hit.point;
                found = true;
            }
        }

        _projected_cursor.SetActive(found);
        if (found)
            _projected_cursor.transform.position = best_point;
    }

    public void ToggleDrawingMode()
    {
        _drawing_mode_enabled = !_drawing_mode_enabled;
        _drawing_ui.SetActive(_drawing_mode_enabled);
        _line_renderer.positionCount = 0;
    }

    /// <summary>
    /// Forces drawing mode to a specific state without toggling and without
    /// clearing the current sketch. Used by scripts (e.g. programmatic sketch
    /// generators) that need the drawing UI and line renderer active before
    /// writing points, without risking flipping an already-on state off.
    /// </summary>
    public void SetDrawingMode(bool enabled)
    {
        _drawing_mode_enabled = enabled;
        _drawing_ui.SetActive(_drawing_mode_enabled);
    }

    public bool IsDrawingModeEnabled() => _drawing_mode_enabled;

    public void ClearSketch()
    {
        _line_renderer.positionCount = 0;
    }
}