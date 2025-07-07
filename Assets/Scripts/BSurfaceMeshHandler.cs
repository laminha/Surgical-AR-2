#pragma warning disable IDE0017 // Simplify object initialization
#pragma warning disable IDE0090 // Simplify object initialization
using UnityEditor;
using UnityEngine;

public class BSurfaceMeshHandler : MonoBehaviour
{
    public BsplineManager _control_point_obj;
    MeshFilter _mesh_filter;
    Vector3[] _vertices;
    public uint _resolution; // Number of quads along one axis per BSurface _size.
    int vertex_per_side = 0;
    int _fps; // Framerate of Unity.
    [ContextMenu("Start")]
    void Start()
    {
        // Get framerate.
        _fps = (int)(1f / Time.fixedDeltaTime);

        // Get mesh filter component.
        _mesh_filter = GetComponent<MeshFilter>();
        if (_mesh_filter == null)
            Debug.LogError("Mesh filter cant be found. -Calvin");

        // Instantiate mesh.
        Mesh mesh = new();
        mesh.name = "BSurfaceMesh";

        // Calculate numbers of quads and vertices.
        int quads_per_side = (_control_point_obj._size - 3) * (int)_resolution;
        vertex_per_side = quads_per_side + 1;
        int quads_total = quads_per_side * quads_per_side;
        int vertex_total = vertex_per_side * vertex_per_side;
        // Define vertices array with uv map.
        _vertices = new Vector3[vertex_total];
        Vector2[] uv_map = new Vector2[vertex_total];
        for (int i = 0; i < vertex_per_side; i++)
            for (int j = 0; j < vertex_per_side; j++)
            {
                float u = Mathf.InverseLerp(0, vertex_per_side - 1, i);
                float v = Mathf.InverseLerp(0, vertex_per_side - 1, j);
                _vertices[i + j * vertex_per_side] = _control_point_obj.CalcBsurface(u, v);

                uv_map[i + j * vertex_per_side] = new Vector2((float)i / (vertex_per_side - 1), (float)j / (vertex_per_side - 1));
            }
        // Define triangle index array.
        int[] triangles = new int[quads_total * 6];
        for (int j = 0; j < quads_per_side; j++)
            for (int i = 0; i < quads_per_side; i++)
            {
                // Calculate starting index for triangle array (there are 2 triangles per quad ie. 6 indices).
                int index_0 = 6 * (i + j * quads_per_side);
                triangles[index_0 + 0] = i + 0 + vertex_per_side * (j + 0);
                triangles[index_0 + 1] = i + 0 + vertex_per_side * (j + 1);
                triangles[index_0 + 2] = i + 1 + vertex_per_side * (j + 0);
                triangles[index_0 + 3] = i + 1 + vertex_per_side * (j + 0);
                triangles[index_0 + 4] = i + 0 + vertex_per_side * (j + 1);
                triangles[index_0 + 5] = i + 1 + vertex_per_side * (j + 1);
            }
        // Assign vertices, uv mapping, and triangles to mesh.
        mesh.vertices = _vertices;
        mesh.uv = uv_map;
        mesh.triangles = triangles;
        // Recalculate normals.
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        // Assign mesh to mesh filter.
        _mesh_filter.mesh = mesh;
    }

    [ContextMenu("Update")]
    void Update()
    {
        // Only run if frame number is a multiple of framerate.
        if (Time.frameCount % (_fps*10) != 0)
            return;

        // Interate through all vertices, moving them to the curve.
        for (int i = 0; i < vertex_per_side; i++)
            for (int j = 0; j < vertex_per_side; j++)
            {
                // Calculate u and v corresponding to vertex (should go from 0 to 1).
                float u = Mathf.InverseLerp(0, vertex_per_side - 1, i);
                float v = Mathf.InverseLerp(0, vertex_per_side - 1, j);
                // Set current vertex coordinates to the curve at uv.
                _vertices[i + j * vertex_per_side] = _control_point_obj.CalcBsurface(u, v);
            }
        // Update mesh.
        Mesh mesh = _mesh_filter.sharedMesh;
        mesh.vertices = _vertices;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        _mesh_filter.mesh = mesh;
    }
    void OnDrawGizmos()
    {
        // Wait for the control point object to be assigned.
        if (_control_point_obj._control_points == null)
            return;
        // Draw the mesh in the scene view.
        Start();
    }

    [ContextMenu("GenerateCircleTexture")]
    void  GenerateCircleTexture()
    {
        int size = 1024;
        Texture2D tex = new Texture2D(size, size, TextureFormat.RGBA32, false);
        float radius = size / 2f;
        Vector2 center = new Vector2(radius, radius);

        const float line_width = 4f; // In pixels.
        const float grid_spacing = 1024 / 12;
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center);
                // Outside circle.
                if (dist > radius)
                    tex.SetPixel(x, y, new Color(0.5f, 0.5f, 0.5f, 0)); // Invisible grey.
                // On circle perimeter or gridlines.
                else if (
                    dist > radius - line_width ||
                    (x + line_width / 2) % grid_spacing < line_width ||
                    (y + line_width / 2) % grid_spacing < line_width
                )
                    tex.SetPixel(x, y, new Color(0, 0, 0, 1)); // Solid black.
                // Inside circle.
                else
                    tex.SetPixel(x, y, new Color(0, 0.2f+0.8f*dist/radius, 0, 0.5f)); // Trasparent color gradient.
            }
        }
        tex.filterMode = FilterMode.Bilinear;
        tex.Apply();

        // Encode texture to PNG
        byte[] pngData = tex.EncodeToPNG();
        if (pngData != null)
        {
            // Save to Assets/Calvin/GeneratedDiskTexture.png
            string path = Application.dataPath + "/Textures/GeneratedDiskTexture.png";
            System.IO.File.WriteAllBytes(path, pngData);
            Debug.Log("Texture saved to: " + path);

            #if UNITY_EDITOR
                    // Refresh the AssetDatabase so the texture appears in the Project window
                    AssetDatabase.Refresh();
            #endif
        }
    }
}
