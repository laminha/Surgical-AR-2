using Oculus.Interaction;
using TMPro;
using UnityEngine;

/// <summary>
/// Attach to each of the 4 buttons inside the scenario submenu.
/// Set _scenario_index in the Inspector per button:
///   0 = Heart
///   1 = Volumetric Muscle Loss
///   2 = Irregular Perimeter Wound
///  -1 = Clear
/// </summary>
public class CallSelectScenario : MonoBehaviour
{
    public ClinicalScenarioManager _scenario_manager;
    public RayInteractable _ray_interactable;

    [Tooltip("-1 = Clear, 0 = Heart, 1 = Volumetric Muscle Loss, 2 = Irregular Perimeter")]
    public int _scenario_index = 0;

    [Tooltip("Label shown on this button. Set in Inspector.")]
    public string _label = "Heart";
    public TextMeshPro _button_text;
    public Color _active_color = Color.green;
    public Color _default_color = Color.white;

    void Start()
    {
        _ray_interactable.WhenSelectingInteractorViewAdded += Selected;
        if (_button_text != null)
            _button_text.text = _label;
    }

    void Selected(IInteractorView arg)
    {
        _scenario_manager.SelectScenario(_scenario_index);
    }

    void Update()
    {
        if (_button_text == null) return;
        bool is_active = _scenario_manager.GetActiveScenarioIndex() == _scenario_index
                         && _scenario_index != -1;
        _button_text.color = is_active ? _active_color : _default_color;
    }
}
