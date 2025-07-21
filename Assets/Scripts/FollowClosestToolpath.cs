using UnityEngine;

public class FollowClosestInLinerenderer : MonoBehaviour {
    public LineRenderer _line_renderer;
    public Transform _target;
    public int _index_of_closest = -1;
    void Update() {
        Vector3 closestPoint = _line_renderer.GetPosition(0);
        for (int i = 0; i < _line_renderer.positionCount; i++) {
            Vector3 point = _line_renderer.GetPosition(i);
            float distance = Vector3.Distance(_target.position, point);
            if (distance < Vector3.Distance(_target.position, closestPoint)) {
                closestPoint = point;
                _index_of_closest = i;
            }
        }
        transform.position = closestPoint;
    }
}
