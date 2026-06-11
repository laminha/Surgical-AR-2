using Oculus.Interaction;
using UnityEngine;

/// <summary>
/// Attach to the "Clinical Scenarios" top-level button.
/// Toggles the scenario selection submenu panel open/closed.
/// </summary>
public class CallClinicalScenarioMenu : MonoBehaviour
{
    public ClinicalScenarioManager _scenario_manager;
    public RayInteractable _ray_interactable;

    void Start()
    {
        _ray_interactable.WhenSelectingInteractorViewAdded += Selected;
    }

    void Selected(IInteractorView arg)
    {
        _scenario_manager.ToggleScenarioMenu();
    }
}
