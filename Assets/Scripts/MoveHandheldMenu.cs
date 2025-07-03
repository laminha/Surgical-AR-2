using Oculus.Interaction;
using UnityEngine;

public class MoveHandheldMenu : MonoBehaviour
{
    public GameObject _object_to_transform;
    public Vector3 _displacement;
    private void Awake()
    {
        Debug.Log("calvin im awake!");

        var interactable = GetComponent<RayInteractable>();

        if (interactable != null)
        {
            Debug.Log("calvin interactable found");
            interactable.WhenPointerEventRaised += MoveTarget;
        }
        else
        {
            Debug.Log("calvin interactable not found");
        }
    }
    private void MoveTarget(PointerEvent pointer_event)
    {

        //Debug.Log("calvin Event triggered");

        // if (pointer_event.Type == PointerEventType.Cancel)
        //     Debug.Log("calvin Cancel");
        // if (pointer_event.Type == PointerEventType.Hover)
        //     Debug.Log("calvin Hover");
        // if (pointer_event.Type == PointerEventType.Move)
        //     Debug.Log("calvin Move");
        if (pointer_event.Type == PointerEventType.Select)
        {
            // Debug.Log("calvin Select");
            _object_to_transform.GetComponent<Follow>()._displacement += _displacement;
        }
        // if (pointer_event.Type == PointerEventType.Unhover)
        //     Debug.Log("calvin Unhover");
        if (pointer_event.Type == PointerEventType.Unselect)
        {
            // Debug.Log("calvin Unselect");
            _object_to_transform.GetComponent<Follow>()._displacement -= _displacement;
        }
    }
}
