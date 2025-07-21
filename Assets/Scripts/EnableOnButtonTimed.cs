using UnityEngine;

public class EnableOnButtonTimed : MonoBehaviour {
    float _last_press_time = -100f;
    public float timeout = 1f;
    FollowClosestInLinerenderer _follow_closest_toolpath;
    MeshRenderer _mesh_renderer;
    void Start() {
        _follow_closest_toolpath = GetComponent<FollowClosestInLinerenderer>();
        _mesh_renderer = GetComponent<MeshRenderer>();
    }

    void Update() {
        if (OVRInput.Get(OVRInput.Button.Two, OVRInput.Controller.RTouch)) {
            _last_press_time = Time.time;
        }
        if (Time.time - _last_press_time > timeout) {
            _follow_closest_toolpath.enabled = false;
            _mesh_renderer.enabled = false;
        }
        else {
            _follow_closest_toolpath.enabled = true;
            _mesh_renderer.enabled = true;
        }
    }
}
