using UnityEngine;

public class DeleteAtPoint : MonoBehaviour
{
    FollowClosestInLinerenderer _closest_index_reference;
    float _time_button_pressed = 0f;
    public float _hold_time_for_delete = 1f;
    BSurfaceGcodeGenerator _gcode_generator;
    public float _default_scale = 0.03f;

    void Start() {
        _closest_index_reference = GetComponent<FollowClosestInLinerenderer>();
        _gcode_generator = FindFirstObjectByType<BSurfaceGcodeGenerator>();
    }

    void Update() {
        bool b_button_pressed = OVRInput.Get(OVRInput.Button.Two, OVRInput.Controller.RTouch);
        bool b_button_just_pressed = OVRInput.GetDown(OVRInput.Button.Two, OVRInput.Controller.RTouch);
        bool b_button_just_released = OVRInput.GetUp(OVRInput.Button.Two, OVRInput.Controller.RTouch);

        if (b_button_just_pressed)
            _time_button_pressed = Time.time;

        if (b_button_just_released)
            transform.localScale = new(_default_scale, _default_scale, _default_scale);

        if (b_button_pressed) {
            _closest_index_reference.enabled = false;
            float dynamic_scale = _default_scale * ((Time.time - _time_button_pressed) / _hold_time_for_delete + 1);
            transform.localScale = new(dynamic_scale, dynamic_scale, dynamic_scale);
        }
        else
            _closest_index_reference.enabled = true;

        if (Time.time - _time_button_pressed > _hold_time_for_delete && b_button_pressed) {
            int closest_index = _closest_index_reference._index_of_closest;
            _time_button_pressed = float.MaxValue;
            _gcode_generator._uv_points.RemoveRange(closest_index, _gcode_generator._uv_points.Count - closest_index);
            Debug.Log("deletingpoint");
        }
    }
}
