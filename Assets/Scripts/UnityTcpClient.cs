using UnityEngine;
using System.Net.Sockets;
using System.Text;

public class UnityTcpClient : MonoBehaviour
{
    public bool _enable_tcp_connection = true;
    public string serverIP = "192.168.10.2"; // <-- Set this to your Ubuntu machine's IP
    public int serverPort = 5005;

    private TcpClient client;
    private NetworkStream stream;

    void Start()
    {
        if (_enable_tcp_connection == false)
        {
            Debug.LogWarning("calvin TCP connection is disabled");
            return;
        }
        try
        {
            client = new TcpClient(serverIP, serverPort);
            stream = client.GetStream();
            Debug.Log("calvin Connected to server");
        }
        catch (SocketException ex)
        {
            Debug.LogError("calvin Socket error: " + ex.Message);
        }
    }
    void OnApplicationQuit()
    {
        stream?.Close();
        client?.Close();
    }
    public void SendTcpMessage(string msg)
    {
        if (_enable_tcp_connection == false)
        {
            Debug.LogWarning("calvin TCP connection is disabled");
            return;
        }
        
        byte[] data = Encoding.UTF8.GetBytes(msg);
        stream.Write(data, 0, data.Length);
        Debug.Log("calvin TCP Sent");
    }
}
