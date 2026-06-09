using Oculus.Interaction;
using UnityEngine;

public class ClearToolpath : MonoBehaviour
{
    public BSurfaceGcodeGenerator _gcode_generator;

    void Start()
    {
        RayInteractable ray = GetComponent<RayInteractable>();
        ray.WhenSelectingInteractorViewAdded += Selected;
    }

    void Selected(IInteractorView arg)
    {
        _gcode_generator.ClearToolpath();
    }
}