using System;
using System.ComponentModel.Design;
using NUnit.Framework.Constraints;
using Oculus.Interaction.Input;
using OVR.OpenVR;
using Unity.XR.CoreUtils;
using UnityEngine;

public class ControlPolygonLiquifyToolManager : MonoBehaviour
{
    public OVRCameraRig _tracking_space_origin;
    public BsplineManager _bspline;
    public OVRInput.Controller _curr_liquify_tool = OVRInput.Controller.None;
    Vector3 _prev_right_position = new();
    Vector3 _prev_left_position = new();
    Quaternion _prev_right_rotation = new();
    Quaternion _prev_left_rotation = new();
    public GameObject _sphere_of_influence;
    public float _influence_radius;

    [ContextMenu("Update")]
    void Update()
    {
        // Use OVR class to read controller position.
        // The last two are local to the UI.
        Vector3 right_controller_local = _tracking_space_origin.rightControllerAnchor.localPosition + Vector3.forward * 0.045f;
        Vector3 left_controller_local = _tracking_space_origin.leftControllerAnchor.localPosition + Vector3.forward * 0.045f;
        Vector3 right_position_world = _tracking_space_origin.rightControllerAnchor.TransformPoint(right_controller_local);
        Vector3 left_position_world = _tracking_space_origin.leftControllerAnchor.TransformPoint(left_controller_local);
        Vector3 right_position = _bspline.transform.InverseTransformPoint(right_position_world);
        Vector3 left_position = _bspline.transform.InverseTransformPoint(left_position_world);

        // Update prev positions.
        Vector3 prev_right_position = _prev_right_position;
        Vector3 prev_left_position = _prev_left_position;
        _prev_right_position = right_position;
        _prev_left_position = left_position;
        Vector3 delta_right = right_position - prev_right_position;
        Vector3 delta_left = left_position - prev_left_position;

        // Read controller orientation.
        Quaternion right_rotation_world = _tracking_space_origin.rightControllerAnchor.rotation;
        Quaternion left_rotation_world = _tracking_space_origin.leftControllerAnchor.rotation;
        Quaternion right_rotation_local = Quaternion.Inverse(_bspline.transform.rotation) * right_rotation_world;
        Quaternion left_rotation_local = Quaternion.Inverse(_bspline.transform.rotation) * left_rotation_world;

        // Update prev rotations.
        Quaternion prev_right_rotation = _prev_right_rotation;
        Quaternion prev_left_rotation = _prev_left_rotation;
        _prev_right_rotation = right_rotation_local;
        _prev_left_rotation = left_rotation_local;
        Quaternion delta_rot_right = right_rotation_local * Quaternion.Inverse(prev_right_rotation);
        Quaternion delta_rot_left = left_rotation_local * Quaternion.Inverse(prev_left_rotation);

        // Update _curr_liquify_tool.
        _sphere_of_influence.transform.localScale = new(_influence_radius * 2, _influence_radius * 2, _influence_radius * 2);
        if (OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.RTouch))
            _curr_liquify_tool = OVRInput.Controller.RTouch;
        if (OVRInput.GetDown(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.LTouch))
            _curr_liquify_tool = OVRInput.Controller.LTouch;
        if (OVRInput.GetUp(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.RTouch) && _curr_liquify_tool == OVRInput.Controller.RTouch)
            _curr_liquify_tool = OVRInput.Controller.None;
        if (OVRInput.GetUp(OVRInput.Button.PrimaryHandTrigger, OVRInput.Controller.LTouch) && _curr_liquify_tool == OVRInput.Controller.LTouch)
            _curr_liquify_tool = OVRInput.Controller.None;

        // Update influence radius.
        if (OVRInput.GetDown(OVRInput.Button.Two, _curr_liquify_tool))
        {
            _influence_radius += 0.01f;
            if (_influence_radius > 0.1f)
                _influence_radius = 0.1f;
        }
        if (OVRInput.GetDown(OVRInput.Button.One, _curr_liquify_tool))
        {
            _influence_radius -= 0.01f;
            if (_influence_radius < 0.01f)
                _influence_radius = 0.01f;
        }

        // Update location/scale of transparent sphere of influence.
        switch (_curr_liquify_tool)
        {
            case OVRInput.Controller.RTouch: 
                _sphere_of_influence.transform.position = right_position_world;
                _sphere_of_influence.SetActive(true);
                break;
            case OVRInput.Controller.LTouch:
                _sphere_of_influence.transform.position = left_position_world;
                _sphere_of_influence.SetActive(true);
                break;
            case OVRInput.Controller.None:
                _sphere_of_influence.SetActive(false);
                break;
        }

        // If current tool's index trigger is pressed, move control points by delta_{tool}.
        Vector3 delta;
        Quaternion delta_rot;
        if ((_curr_liquify_tool == OVRInput.Controller.LTouch) && OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.LTouch))
        {
            delta = delta_left;
            delta_rot = delta_rot_left;
        }
        else if ((_curr_liquify_tool == OVRInput.Controller.RTouch) && OVRInput.Get(OVRInput.Button.PrimaryIndexTrigger, OVRInput.Controller.RTouch))
        {
            delta = delta_right;
            delta_rot = delta_rot_right;
        }
        else
        {
            delta = Vector3.zero;
            delta_rot = Quaternion.identity;
        }

        // Define which controller we are taking each point's distance to.
        Vector3 tool_position;
        if (_curr_liquify_tool == OVRInput.Controller.LTouch)
            tool_position = left_position;
        else if (_curr_liquify_tool == OVRInput.Controller.RTouch)
            tool_position = right_position;
        else
            tool_position = Vector3.zero;

        // Then iterate through all control points, moving them slower to further away they are.
        const float empirical_scalar = 1.0f;
        int num_points = _bspline._size;
        for (int i = 0; i < num_points; i++)
            for (int j = 0; j < num_points; j++)
            {
                // Alter based on delta position.
                float distance = Vector3.Distance(tool_position, _bspline._control_points[i, j]);
                float weight = 0;
                if (distance < _influence_radius)
                {
                    float dpr = distance / _influence_radius;
                    float dpr2 = dpr * dpr;
                    float dpr3 = dpr2 * dpr;
                    weight = 1 - 3 * dpr2 + 2 * dpr3;
                }
                _bspline._control_points[i, j] += empirical_scalar * weight * delta;

                // Alter based on delta rotation.
                Vector3 dir = _bspline._control_points[i, j] - tool_position;
                Quaternion weighted_delta_rot = Quaternion.Slerp(Quaternion.identity, delta_rot, weight);
                Vector3 new_dir = weighted_delta_rot * dir;
                _bspline._control_points[i, j] = tool_position + new_dir;
            }
    }
}
