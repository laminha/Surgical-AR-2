using System.Net.Sockets;
using Meta.XR.ImmersiveDebugger.UserInterface.Generic;
using UnityEngine;

public class LiveControlGcodeSender : MonoBehaviour
{
    const int kSendRate = 10;
    float _next_send_time = 0;
    public GameObject _toolpath_ui;
    public GameObject _live_control_ui;
    public UnityTcpClient _target_tcp_client;
    public Transform _virtual_psm_transform;
    public LiveControlManipulationHandler _manipulation_handler;
    float prev_x = 0; float prev_y = 0; float prev_z = 0;
    float prev_a = 0; float prev_b = 0; float prev_c = 0; float prev_d = 0;
    public void ToggleLiveControl()
    {
        if (_live_control_ui.activeSelf)
        {
            _toolpath_ui.SetActive(true);
            _live_control_ui.SetActive(false);
        }
        else
        {
            _toolpath_ui.SetActive(false);
            _live_control_ui.SetActive(true);
        }
    }
    void Update()
    {
        // Only perform live control if it's on and it's time to.
        if (Time.time < _next_send_time || _live_control_ui.activeSelf == false)
            return;
        _next_send_time = Time.time + 1.0f / kSendRate;

        // Leave if clutch is on.
        if (_manipulation_handler._clutch == true)
            return;

        // Get values for a gcode rapid.
        Vector3 curr_position = _virtual_psm_transform.localPosition;
        Vector3 curr_rpy_rot = _virtual_psm_transform.localEulerAngles;
        float psm_x = -curr_position.x * 1000;
        float psm_y = -curr_position.z * 1000;
        float psm_z = curr_position.y * 1000;
        float psm_roll = curr_rpy_rot.z; // Remember that Unity is LHR for rotations.
        float psm_pitch = curr_rpy_rot.x;
        float psm_yaw = -curr_rpy_rot.y;
        // Bound rotations to (-180, 180].
        if (psm_roll > 180)
            psm_roll -= 360;
        if (psm_pitch > 180)
            psm_pitch -= 360;
        if (psm_yaw < -180)
            psm_yaw += 360;
        // Get jaw position and convert to dVRK value (1f=open=3f, 0f=closed=0f, normally closed).
        float jaw_pos = 3f * _manipulation_handler._jaw_control;

        // Escape if the new values are too close to the old ones (0.1 resolution should be enough).
        if (Mathf.Abs(psm_x - prev_x) < 0.1f &&
            Mathf.Abs(psm_y - prev_y) < 0.1f &&
            Mathf.Abs(psm_z - prev_z) < 0.1f &&
            Mathf.Abs(psm_roll - prev_a) < 0.1f &&
            Mathf.Abs(psm_pitch - prev_b) < 0.1f &&
            Mathf.Abs(psm_yaw - prev_c) < 0.1f &&
            Mathf.Abs(jaw_pos - prev_d) < 0.1f)
        {
            return;
        }

        // Create message.
        string msg = $"G00 X{psm_x} Y{psm_y} Z{psm_z} A{psm_roll} B{psm_pitch} C{psm_yaw} D{jaw_pos}\n";
        _target_tcp_client.SendTcpMessage(msg);

        // Update prev values.
        prev_x = psm_x;
        prev_y = psm_y;
        prev_z = psm_z;
        prev_a = psm_roll;
        prev_b = psm_pitch;
        prev_c = psm_yaw;
        prev_d = jaw_pos;
    }
}
