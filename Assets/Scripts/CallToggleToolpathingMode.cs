using Oculus.Interaction;
using UnityEngine;

public class CallToggleToolpathingMode : MonoBehaviour
{
    public ConformalToolpathingModeManager _target_toolpathing_mode_manager;
    public RayInteractable _ray_interactable;
    void Start()
    {
        _ray_interactable.WhenSelectingInteractorViewAdded += Selected;
    }
    void Selected(IInteractorView arg)
    {
        _target_toolpathing_mode_manager.ToggleToolpathingMode();
    }
}
