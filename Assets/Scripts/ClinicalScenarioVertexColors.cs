using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Applies vertex colors to the clinical scenario anatomy meshes.
/// Colors are assigned based on vertex height (y position) so the VML divot
/// is visually distinct from the surrounding surface.
/// Attach to the same GameObject as ClinicalScenarioManager and ClinicalScenarioMeshGenerator.
/// Call ApplyAll() after GenerateAll() has run.
/// </summary>
[RequireComponent(typeof(ClinicalScenarioManager))]
[RequireComponent(typeof(ClinicalScenarioMeshGenerator))]
public class ClinicalScenarioVertexColors : MonoBehaviour
{
    [Header("Heart colors")]
    public Color _heart_color = new Color(0.85f, 0.2f, 0.2f); // Deep red

    [Header("VML colors")]
    public Color _vml_surface_color = new Color(0.8f, 0.3f, 0.2f);  // Bright muscle red
    public Color _vml_divot_color   = new Color(0.1f, 0.5f, 0.1f);  // Green — unmistakably different

    [Header("Irregular colors")]
    public Color _irr_center_color = new Color(0.9f, 0.75f, 0.65f); // Skin/tissue
    public Color _irr_edge_color   = new Color(0.7f, 0.4f, 0.3f);   // Darker edge

    [Header("Real heart colors")]
    //public Color _real_heart_color = new Color(0.85f, 0.2f, 0.2f, 0.5f); // red, semi-transparent
    public Material _real_heart_material;

    void Start()
    {
        ApplyAll();
    }

    [ContextMenu("ApplyAll")]
    public void ApplyAll()
    {
        ClinicalScenarioManager mgr = GetComponent<ClinicalScenarioManager>();
        if (mgr == null) return;

        if (mgr._anatomy_heart     != null) ApplyHeartColors(mgr._anatomy_heart);
        if (mgr._anatomy_scenario2 != null) ApplyVMLColors(mgr._anatomy_scenario2);
        if (mgr._anatomy_scenario3 != null) ApplyIrregularColors(mgr._anatomy_scenario3);
        if (mgr._anatomy_scenario4 != null) ApplyRealHeartColors(mgr._anatomy_scenario4);
    }

    // -------------------------------------------------------------------------
    // Shared helper — creates a URP vertex color material and assigns it
    // -------------------------------------------------------------------------

   static Material CreateVertexColorMaterial()
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Particles/Unlit");
        if (shader == null)
        {
            Debug.LogWarning("ClinicalScenarioVertexColors: falling back to URP/Unlit.");
            shader = Shader.Find("Universal Render Pipeline/Unlit");
        }
        if (shader == null)
        {
            Debug.LogError("ClinicalScenarioVertexColors: No suitable shader found.");
            return null;
        }
        Material mat = new Material(shader);
        mat.SetFloat("_Surface", 0);       // Opaque
        mat.SetFloat("_ColorMode", 4f);    // 4 = Color mode
        mat.SetColor("_BaseColor", Color.white);  // White base so vertex colors show through
        mat.SetColor("_EmissionColor", Color.black);
        return mat;
    }

    static void AssignVertexColors(GameObject target, Color[] colors)
    {
        MeshFilter mf = target.GetComponent<MeshFilter>();
        if (mf == null || mf.mesh == null) return;

        Mesh mesh = mf.mesh;
        if (colors.Length != mesh.vertexCount)
        {
            Debug.LogError($"ClinicalScenarioVertexColors: color array length {colors.Length} " +
                           $"does not match vertex count {mesh.vertexCount} on {target.name}.");
            return;
        }
        mesh.colors = colors;
        mesh.UploadMeshData(false); // Ensure color data is pushed to GPU.

        MeshRenderer mr = target.GetComponent<MeshRenderer>();
        if (mr == null) return;

        Material mat = CreateVertexColorMaterial();
        if (mat != null)
            mr.material = mat;
    }

    // -------------------------------------------------------------------------
    // Heart — uniform color, slight pole darkening
    // -------------------------------------------------------------------------

    void ApplyHeartColors(GameObject target)
    {
        MeshFilter mf = target.GetComponent<MeshFilter>();
        if (mf == null || mf.mesh == null) return;

        Mesh mesh = mf.mesh;
        Vector3[] verts = mesh.vertices;
        Color[] colors = new Color[verts.Length];

        // Find y extents.
        float y_min = float.MaxValue, y_max = float.MinValue;
        foreach (Vector3 v in verts) { y_min = Mathf.Min(y_min, v.y); y_max = Mathf.Max(y_max, v.y); }

        for (int i = 0; i < verts.Length; i++)
        {
            float t = Mathf.InverseLerp(y_min, y_max, verts[i].y);
            // Slightly darker at poles, brighter at equator.
            colors[i] = Color.Lerp(_heart_color * 0.7f, _heart_color, t);
        }

        AssignVertexColors(target, colors);
    }

    // -------------------------------------------------------------------------
    // VML — dark in divot, muscle color on flat surface
    // -------------------------------------------------------------------------

    void ApplyVMLColors(GameObject target)
    {
        MeshFilter mf = target.GetComponent<MeshFilter>();
        if (mf == null || mf.mesh == null) return;

        Mesh mesh = mf.mesh;
        Vector3[] verts = mesh.vertices;
        int[] tris = mesh.triangles;

        // Split triangles into divot (low y) and surface (high y) groups.
        ClinicalScenarioMeshGenerator gen = GetComponent<ClinicalScenarioMeshGenerator>();
        float threshold = -(gen._vml_defect_depth * 0.3f); // Top 30% of defect depth = divot zone.

        List<int> divot_tris   = new List<int>();
        List<int> surface_tris = new List<int>();

        for (int i = 0; i < tris.Length; i += 3)
        {
            Vector3 v0 = verts[tris[i]];
            Vector3 v1 = verts[tris[i + 1]];
            Vector3 v2 = verts[tris[i + 2]];
            float avg_y = (v0.y + v1.y + v2.y) / 3f;

            if (avg_y < threshold)
            {
                divot_tris.Add(tris[i]);
                divot_tris.Add(tris[i + 1]);
                divot_tris.Add(tris[i + 2]);
            }
            else
            {
                surface_tris.Add(tris[i]);
                surface_tris.Add(tris[i + 1]);
                surface_tris.Add(tris[i + 2]);
            }
        }

        // Assign two submeshes.
        mesh.subMeshCount = 2;
        mesh.SetTriangles(surface_tris, 0);
        mesh.SetTriangles(divot_tris,   1);

        // Create two plain materials.
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        Material surface_mat = new Material(shader);
        Material divot_mat   = new Material(shader);
        surface_mat.color = new Color(0.8f, 0.3f, 0.2f); // Muscle red
        divot_mat.color   = new Color(0.2f, 0.6f, 0.8f); // Blue — unmistakably different
        surface_mat.SetFloat("_Cull", 0f); // 0 = Off (render both sides)
        divot_mat.SetFloat("_Cull", 0f);

        MeshRenderer mr = target.GetComponent<MeshRenderer>();
        mr.materials = new Material[] { surface_mat, divot_mat };
    }

    // -------------------------------------------------------------------------
    // Irregular — bright center, darker toward irregular edge
    // -------------------------------------------------------------------------

    void ApplyIrregularColors(GameObject target)
    {
        MeshFilter mf = target.GetComponent<MeshFilter>();
        if (mf == null || mf.mesh == null) return;

        Mesh mesh = mf.mesh;
        Vector3[] verts = mesh.vertices;
        Color[] colors = new Color[verts.Length];

        // Find max radius for normalization.
        float max_r = 0f;
        foreach (Vector3 v in verts)
            max_r = Mathf.Max(max_r, new Vector2(v.x, v.z).magnitude);

        for (int i = 0; i < verts.Length; i++)
        {
            float r = new Vector2(verts[i].x, verts[i].z).magnitude;
            float t = (max_r > 0f) ? Mathf.InverseLerp(0f, max_r, r) : 0f;
            t = Mathf.SmoothStep(0f, 1f, t);
            colors[i] = Color.Lerp(_irr_center_color, _irr_edge_color, t);
        }

        MeshFilter mf2 = target.GetComponent<MeshFilter>();
        if (mf2 == null || mf2.mesh == null) return;
        mf2.mesh.colors = colors;

        MeshRenderer mr = target.GetComponent<MeshRenderer>();
        if (mr == null) return;

        Material mat = CreateVertexColorMaterial();
        if (mat != null)
        {
            mat.SetFloat("_Cull", 0f);
            mr.material = mat;
        }
    }

    void ApplyRealHeartColors(GameObject target)
    {
        MeshRenderer mr = target.GetComponent<MeshRenderer>();
        if (mr == null) return;

        if (_real_heart_material != null)
            mr.material = _real_heart_material;
    }
    /*
    void ApplyRealHeartColors(GameObject target)
    {
        MeshRenderer mr = target.GetComponent<MeshRenderer>();
        if (mr == null)
        {
            Debug.LogError("No MeshRenderer found on " + target.name);
            return;
        }

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
        {
            Debug.LogError("URP/Lit shader not found.");
            return;
        }

        Material mat = new Material(shader);
        mat.color = new Color(0.85f, 0.2f, 0.2f, 1f);
        mat.SetFloat("_Cull", 0f);
        mr.material = mat;
    }
    */
    }
