using Oculus.Interaction;
using UnityEngine;

public class CallLiveControlToggle : MonoBehaviour
{
    public LiveControlGcodeSender _target_live_control_handler;
    public RayInteractable _ray_interactable;
    void Start()
    {
        _ray_interactable.WhenSelectingInteractorViewAdded += Selected;
    }
    void Selected(IInteractorView arg)
    {
        // Call Gcode perform.
        _target_live_control_handler.ToggleLiveControl();
    }
}
