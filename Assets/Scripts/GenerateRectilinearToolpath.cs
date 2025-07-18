using Oculus.Interaction;
using TMPro;
using UnityEngine;

public class GenerateRectilinearToolpath : MonoBehaviour {
    public TextMeshPro _button_text;
    BSurfaceGcodeGenerator _gcode_generator;
    ConformalToolpathingManager _toolpathing_manager;
    OVRCameraRig _camera_rig;
    RayInteractable _ray_interactable;
    public int _rectilinear_lines_queued = 0;
    GenerateConcentricToolpath _concentric_toolpath_generator;

    void Awake() {
        _gcode_generator = FindFirstObjectByType<BSurfaceGcodeGenerator>();
        _toolpathing_manager = FindFirstObjectByType<ConformalToolpathingManager>();
        _camera_rig = FindFirstObjectByType<OVRCameraRig>();
        _ray_interactable = GetComponent<RayInteractable>();
        _concentric_toolpath_generator = FindFirstObjectByType<GenerateConcentricToolpath>();

        _ray_interactable.WhenSelectingInteractorViewAdded += Selected;
    }

    void Selected(IInteractorView arg) {
        _rectilinear_lines_queued++;
        _concentric_toolpath_generator._concentric_rings_queued = 0;
    }

    void Update() {
        if (_rectilinear_lines_queued > 0)
            _button_text.text = $"Rectilinear\nLines\nQueued:\n{_rectilinear_lines_queued}";
        else
            _button_text.text = "Prepare\nRectilinear\nToolpath";
    }
}
