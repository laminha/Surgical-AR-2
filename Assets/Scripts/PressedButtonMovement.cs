using Oculus.Interaction;
using UnityEngine;

public class PressedButtonMovement : MonoBehaviour
{
    RayInteractable _ray_interactable;
    public Vector3 _local_move_offset = new(0, 0, 4);
    Vector3 _local_original_pos; 
    void Start()
    {
        if (_ray_interactable == null)
        {
            _ray_interactable = GetComponent<RayInteractable>();
        }
        _local_original_pos = transform.localPosition;
        _ray_interactable.WhenSelectingInteractorViewAdded += OnSelected;
        _ray_interactable.WhenSelectingInteractorViewRemoved += OnUnselected;
    }
    public void OnSelected(IInteractorView args)
    {
        transform.localPosition = _local_original_pos + _local_move_offset;
    }
    public void OnUnselected(IInteractorView args)
    {
        transform.localPosition = _local_original_pos;
    }
}
