using Oculus.Interaction;
using TMPro;
using UnityEngine;

public class GenerateConcentricToolpath : MonoBehaviour {
    public TextMeshPro _button_text;
    BSurfaceGcodeGenerator _gcode_generator;
    ConformalToolpathingManager _toolpathing_manager;
    OVRCameraRig _camera_rig;
    RayInteractable _ray_interactable;
    public int _concentric_rings_queued = 0;

    void Awake() {
        _gcode_generator = FindFirstObjectByType<BSurfaceGcodeGenerator>();
        _toolpathing_manager = FindFirstObjectByType<ConformalToolpathingManager>();
        _camera_rig = FindFirstObjectByType<OVRCameraRig>();
        _ray_interactable = GetComponent<RayInteractable>();

        _ray_interactable.WhenSelectingInteractorViewAdded += Selected;
    }

    void Selected(IInteractorView arg) {
        _concentric_rings_queued++;
    }

    void Update() {
        if (_concentric_rings_queued > 0)
            _button_text.text = $"Concentric\nRings\nQueued:\n{_concentric_rings_queued}";
        else
            _button_text.text = "Prepare\nConcentric\nToolpath";
    }
}