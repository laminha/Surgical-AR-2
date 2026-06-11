using Oculus.Interaction;
using UnityEngine;

public class CallFitSurfaceToDrawing : MonoBehaviour
{
    public SurfaceFittingManager _fitting_manager;
    public RayInteractable _ray_interactable;
    public bool _elliptical_mode = true;
    public BSurfaceMeshHandler _bsurface_mesh_handler;
    void Start()
    {
        _ray_interactable.WhenSelectingInteractorViewAdded += Selected;
    }
    void Selected(IInteractorView arg)
    {
        if (_elliptical_mode)
        {
            _fitting_manager.FitSurfaceToDrawingEllipticalHeuristic();
        }
        else
        {
            _fitting_manager.FitSurfaceToDrawingLeastSquares();
        }
        _bsurface_mesh_handler.gameObject.SetActive(true);
    }
}
