using UnityEngine;

public class MoveJaws : MonoBehaviour
{
    public LiveControlManipulationHandler _control_handler;
    private Transform _jaw1;
    private Transform _jaw2;

    void Start()
    {
        _jaw1 = transform.GetChild(0);
        _jaw2 = transform.GetChild(1);
    }
    void Update()
    {
        // Define the angle of each jaw, and rotations corresponding to those angles.
        float angle_per_jaw = 90f * _control_handler._jaw_control; // 1 = open = 90 deg, 0 = closed = 0 deg.
        Quaternion angle_jaw1 = Quaternion.Euler(0, 0, angle_per_jaw);
        Quaternion angle_jaw2 = Quaternion.Euler(0, 0, -angle_per_jaw);

        _jaw1.SetLocalPositionAndRotation(angle_jaw1 * (0.65f*Vector3.down), angle_jaw1);
        _jaw2.SetLocalPositionAndRotation(angle_jaw2 * (0.65f*Vector3.down), angle_jaw2);
    }
}
