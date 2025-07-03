using UnityEngine;

public class BezierSurfaceRenderer : MonoBehaviour
{
    public Vector3[] _vertices;
    public Vector2[] _uv;
    public int[] _triangles;

    [ContextMenu("Start")]
    void Start()
    {
        Mesh mesh = new Mesh();
        GetComponent<MeshFilter>().mesh = mesh;
        mesh.vertices = _vertices;
        mesh.uv = _uv;
        mesh.triangles = _triangles;
    }

    // [ContextMenu("DebugSetMesh")]
    // void DebugSetMesh()
    // {
    //     //_vertices = new Vector3[3];
    //     //_uv = new Vector2[3];
    //     //_triangles = new int[3];
    //     Vector3[] normals = mesh.normals;

    //     for (int i = 0; i < _vertices.Length; i++)
    //     {
    //         _vertices[i] += normals[i] * Mathf.Sin(Time.time);
    //     }

    //     mesh.vertices = _vertices;
    // }

    void Update()
    {

    }
}
