//using System.Numerics;
using Oculus.Interaction;
using UnityEditorInternal;

//using Unity.Mathematics;
using UnityEngine;
//using UnityEngine.AI;

public class RayGrabbing : MonoBehaviour
{
    public RayInteractable _interactable_object;
    private GameObject _anchor;
    private bool _follow = false;
    private Vector3 _offset_position;
    private Quaternion _offset_rotation;
    public bool _lock_rotation = false;
    void Start()
    {
        _interactable_object.WhenSelectingInteractorViewRemoved += UnselectedHandler;
        _interactable_object.WhenSelectingInteractorViewAdded += SelectedHandler;
    }
    void SelectedHandler(IInteractorView arg)
    {
        var component = arg as Component;
        _anchor = component.gameObject.GetComponent<Pointer>()._pointer;

        _follow = true;

        _offset_position = _anchor.transform.InverseTransformPoint(transform.position);
        _offset_rotation = Quaternion.Inverse(_anchor.transform.rotation) * transform.rotation;
    }
    void UnselectedHandler(IInteractorView arg)
    {
        _follow = false;
    }

    // Update is called once per frame
    void Update()
    {
        if (_follow == false)
            return;

        Vector3 target_position = _anchor.transform.TransformPoint(_offset_position);
        Quaternion target_rotation = _anchor.transform.rotation * _offset_rotation;
        transform.position = Vector3.Lerp(_anchor.transform.position, target_position, 1);
        if (_lock_rotation == true)
            transform.rotation = Quaternion.Lerp(transform.rotation, target_rotation, 1);
    }
}
