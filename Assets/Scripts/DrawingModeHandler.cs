using UnityEngine;

public class DrawingModeHandler : MonoBehaviour
{
    bool _drawing_mode_enabled;
    public GameObject _drawing_ui;
    public GameObject _drawing_cursor;
    public OVRCameraRig _tracking_space;
    public LineRenderer _line_renderer;
    void Awake()
    {
        _drawing_mode_enabled = false;
        if (_drawing_ui == null)
            Debug.LogError("Drawing UI isnt set in DrawingModeHandler.");
        if (_tracking_space == null)
            Debug.LogError("Tracking space isnt set in DrawingModeHandler.");
        if(_line_renderer == null)
            Debug.LogError("Line renderer isnt set in DrawingModeHandler.");
    }
    void Update()
    {
        // Make cursor follow cursor position.
        _drawing_cursor.transform.position = _tracking_space.rightControllerAnchor.TransformPoint(_tracking_space.rightControllerAnchor.localPosition + Vector3.forward * 0.1f);
    }
    public void ToggleDrawingMode()
    {
        // Toggle.
        _drawing_mode_enabled = !_drawing_mode_enabled;
        if (_drawing_mode_enabled)
            _drawing_ui.SetActive(true);
        else
            _drawing_ui.SetActive(false);

        // Clear line renderer (the drawing).
        _line_renderer.positionCount = 0;
    }
}
