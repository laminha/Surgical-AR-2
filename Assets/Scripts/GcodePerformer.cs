using UnityEngine;
using System.IO;
using System.IO.Enumeration;

public class GcodePerformer : MonoBehaviour
{
    public UnityTcpClient _target_tcp_client;
    private string _file_name;
    private string _file_path;
    void Start()
    {
        _file_name = "output.gcode";
        _file_path = Path.Combine(Application.persistentDataPath, _file_name);
    }
    public void PerformGcode()
    {
        string gcode_data = File.ReadAllText(_file_path);
        _target_tcp_client.SendTcpMessage(gcode_data);

        Debug.Log("calvin gcode performed\n" + gcode_data + "\nin\n" + _file_path);
    }
}
