using Oculus.Interaction;
using UnityEngine;
using TMPro;

public class ToggleFreehandToolpath : MonoBehaviour {
    public ConformalToolpathingManager _toolpathing_manager;
    public RayInteractable _ray_interactable;
    public TextMeshPro _button_text;
    public Color _active_color = Color.green;
    public Color _default_color = Color.white;

    void Start() {
        _ray_interactable.WhenSelectingInteractorViewAdded += Selected;
    }

    void Selected(IInteractorView arg) {
        _toolpathing_manager.ToggleFreehandDrawing();
    }

    void Update() {
        if (_button_text == null) return;
        bool is_active = _toolpathing_manager.IsFreehandEnabled();
        _button_text.color = is_active ? _active_color : _default_color;
        _button_text.text = is_active ? "Freehand\nON" : "Freehand\nOFF";
    }
}