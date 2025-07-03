using Oculus.Interaction;
using UnityEngine;

public class CallGcodePerform : MonoBehaviour
{
    public GcodePerformer _target_gcode_performer;
    public RayInteractable _ray_interactable;
    void Start()
    {
        _ray_interactable.WhenSelectingInteractorViewAdded += Selected;
    }
    void Selected(IInteractorView arg)
    {
        // Call Gcode perform.
        _target_gcode_performer.PerformGcode();
    }
}
