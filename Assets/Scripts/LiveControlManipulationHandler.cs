#pragma warning disable IDE0079 // Remove unnecessary suppression
#pragma warning disable IDE0044 // Add readonly modifier
#pragma warning disable UNT0036 // Inefficient position/rotation read
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Assertions.Must;
using UnityEngine.Timeline;

public class LiveControlManipulationHandler : MonoBehaviour
{
    public bool _clutch = true;
    public bool _speed_clutch = false;
    public GameObject _live_control_ui;
    public OVRCameraRig _tracking_space_origin;
    const float _lpf_factor = 0.1f;
    public float _jaw_control = 0f; // 1 = open, 0 = closed.
    Vector3 _prev_controller_pos = Vector3.zero;
    Quaternion _prev_controller_rot = Quaternion.identity;
    Vector3 _target_pos;
    Quaternion _target_rot;
    void Start()
    {
        _prev_controller_pos = _tracking_space_origin.rightControllerAnchor.position;
        _prev_controller_rot = _tracking_space_origin.rightControllerAnchor.rotation;
        _target_pos = transform.position;
        _target_rot = transform.rotation;
    }
    // Grip down clutches, grip up unclutches, a button clutches, trigger is jaw articulation.
    void Update()
    {
        // Define variables.
        Vector3 controller_pos = _tracking_space_origin.rightControllerAnchor.position;
        Quaternion controller_rot = _tracking_space_origin.rightControllerAnchor.rotation;

        // Update clutch
        if (OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.RTouch))
            _clutch = true;
        else if (OVRInput.GetDown(OVRInput.Button.One, OVRInput.Controller.RTouch))
            _clutch = true;
        else if (OVRInput.GetUp(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.RTouch))
            _clutch = false;

        // Update speed clutch.
        if (OVRInput.Get(OVRInput.Button.Two, OVRInput.Controller.RTouch))
            _speed_clutch = true;
        else
            _speed_clutch = false;

        // Update speed factor.
        float speed_factor = _speed_clutch ? 1f : 0.2f;

        // Update controller delta position.
        if (_prev_controller_pos == Vector3.zero)
            _prev_controller_pos = controller_pos;
        if (_prev_controller_rot == Quaternion.identity)
            _prev_controller_rot = controller_rot;
        Vector3 controller_delta_pos = controller_pos - _prev_controller_pos;
        Quaternion controller_delta_rot = controller_rot * Quaternion.Inverse(_prev_controller_rot);
        _prev_controller_pos = controller_pos;
        _prev_controller_rot = controller_rot;

        // Exit if clutched.
        if (_clutch)
            return;

        // Update _jaw_control.
        float raw_jaw = OVRInput.Get(OVRInput.Axis1D.PrimaryIndexTrigger, OVRInput.Controller.RTouch);
        _jaw_control = Mathf.Lerp(0f, 0.5f, raw_jaw); // Limit jaw movement to (fully closed - 50% open).

        // Update target transform.
        _target_pos = Vector3.Lerp(_target_pos, _target_pos + controller_delta_pos, speed_factor);
        _target_rot = Quaternion.Slerp(_target_rot, controller_delta_rot * _target_rot, speed_factor);

        // LPF logic.
        transform.SetPositionAndRotation(
            Vector3.Lerp(transform.position, _target_pos, _lpf_factor),
            Quaternion.Slerp(transform.rotation, _target_rot, _lpf_factor)
        );
  }
}
