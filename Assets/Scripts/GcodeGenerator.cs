using System.IO;
using UnityEngine;

public class GcodeGenerator : MonoBehaviour
{
    public LineRenderer _line_renderer;
    private string _file_name;
    private string _file_path;
    void Start()
    {
        _file_name = "output.gcode";
        _file_path = Path.Combine(Application.persistentDataPath, _file_name);

        //Debug.Log("calvin " + _file_path);
    }
    public void CreateGcode()
    {
        //Debug.Log("calvin gcode creation called");
        float feed_rate = 2000;
        string gcode_movements = ";Top\n";
        for (int i = 0; i < _line_renderer.positionCount; i++)
        { 
            float delta_x = _line_renderer.GetPosition(i).x - _line_renderer.GetPosition(0).x;
            float delta_y = _line_renderer.GetPosition(i).y - _line_renderer.GetPosition(0).y;
            float delta_z = _line_renderer.GetPosition(i).z - _line_renderer.GetPosition(0).z;

            // Create G1 command (change unit vectors around)
            gcode_movements += "G1 X";
            gcode_movements += -delta_x*1000;
            gcode_movements += " Y";
            gcode_movements += -delta_z*1000;
            gcode_movements += " Z";
            gcode_movements += delta_y*1000;
            gcode_movements += " F";
            gcode_movements += feed_rate;
            gcode_movements += "\n";
        }

        Debug.Log("calvin gcode written\n" + gcode_movements + "\nin\n" + _file_path);
        File.WriteAllText(_file_path, gcode_movements);
    }
}
