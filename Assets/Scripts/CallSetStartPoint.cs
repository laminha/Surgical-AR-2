using Oculus.Interaction;
using UnityEngine;
using TMPro;

public class CallSetStartPoint : MonoBehaviour {
    public ConformalToolpathingManager _toolpathing_manager;
    public RayInteractable _ray_interactable;
    public TextMeshPro _button_text;
    public Color _active_color = Color.green;
    public Color _default_color = Color.white;

    void Start() {
        _ray_interactable.WhenSelectingInteractorViewAdded += Selected;
    }

    void Selected(IInteractorView arg) {
        _toolpathing_manager.EnterSelectStartMode();
    }

    void Update() {
        if (_button_text == null) return;
        bool in_select_mode = _toolpathing_manager.IsInSelectStartMode();
        _button_text.color = in_select_mode ? _active_color : _default_color;
        _button_text.text = in_select_mode ? "Selecting\nStart\nPoint" : "Set\nStart\nPoint";
    }
}