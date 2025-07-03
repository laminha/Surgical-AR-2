using Oculus.Interaction;
using UnityEngine;

public class CallGcodeGenerate : MonoBehaviour
{
    public GcodeGenerator _target_generator;
    public RayInteractable _ray_interactable;
    void Start()
    {
        _ray_interactable.WhenSelectingInteractorViewAdded += Selected;
    }
    void Selected(IInteractorView arg)
    {
        // Call Gcode generate.
        _target_generator.CreateGcode();
    }
}
