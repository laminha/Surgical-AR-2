using Oculus.Interaction;
using UnityEngine;
using TMPro;
using System.Collections;

public class CallClearWorkspace : MonoBehaviour
{
    public DrawingModeHandler _drawing_mode_handler;
    public BSurfaceGcodeGenerator _gcode_generator;
    public SurfaceFittingManager _surface_fitting_manager;
    public BsplineManager _bspline_manager;
    public TextMeshPro _button_text;
    public string _default_text = "Clear\nWorkspace";
    public float _feedback_duration = 1f;
    public BSurfaceMeshHandler _bsurface_mesh_handler;

    void Start()
    {
        RayInteractable ray = GetComponent<RayInteractable>();
        ray.WhenSelectingInteractorViewAdded += Selected;
    }

    void Selected(IInteractorView arg)
    {
        _drawing_mode_handler.ClearSketch();
        _surface_fitting_manager._drawn_loop.positionCount = 0;
        _surface_fitting_manager.ClearRawLoop();
        _gcode_generator.ClearToolpath();
        _bspline_manager.ResetSurface();
        _bsurface_mesh_handler.gameObject.SetActive(false);
        StartCoroutine(ShowFeedback());
    }

    IEnumerator ShowFeedback()
    {
        _button_text.text = "Clearing...";
        _button_text.color = Color.green;
        yield return new WaitForSeconds(_feedback_duration);
        _button_text.text = _default_text;
        _button_text.color = Color.white;
    }
}