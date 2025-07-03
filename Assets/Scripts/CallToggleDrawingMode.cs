using Oculus.Interaction;
using UnityEngine;

public class CallToggleDrawingMode : MonoBehaviour
{
    public DrawingModeHandler _target_live_control_handler;
    public RayInteractable _ray_interactable;
    void Start()
    {
        _ray_interactable.WhenSelectingInteractorViewAdded += Selected;
    }
    void Selected(IInteractorView arg)
    {
        _target_live_control_handler.ToggleDrawingMode();
    }
}