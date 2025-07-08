using UnityEngine;

public class ConformalToolpathingModeManager : MonoBehaviour
{
    public BSurfaceGcodeGenerator _gcode_generator;
    public GameObject _toolpathing_ui;
    public void ToggleToolpathingMode()
    {
        _toolpathing_ui.SetActive(!_toolpathing_ui.activeSelf);
        _gcode_generator._uv_points.Clear();
    }
}
