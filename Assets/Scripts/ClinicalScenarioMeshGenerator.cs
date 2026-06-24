using UnityEngine;

/// <summary>
/// Procedurally generates three clinical scenario meshes at runtime and assigns them
/// to the anatomy GameObjects referenced by ClinicalScenarioManager.
///
/// Attach this script to the same GameObject as ClinicalScenarioManager.
/// Call GenerateAll() from Awake, or let it run automatically via Awake.
///
/// Scenario 0 — Heart (epicardial surface):
///   Oblate ellipsoid, ~6x4x5 cm, convex curved surface.
///
/// Scenario 1 — Volumetric Muscle Loss (VML):
///   Rectangular muscle block ~8x3x4 cm with a gaussian concave defect on the top face.
///
/// Scenario 2 — Irregular Perimeter (Calvarial Bone Defect):
///   Slightly oblate spherical cap representing the outer skull surface (~9 cm across,
///   ~1.5 cm dome rise). The surface geometry is smooth and regular — irregularity
///   enters via the surgeon-drawn loop, not the anatomy mesh.
/// </summary>
/// 
[RequireComponent(typeof(ClinicalScenarioManager))]
public class ClinicalScenarioMeshGenerator : MonoBehaviour
{
    [Header("References — set by ClinicalScenarioManager or manually")]
    public GameObject _anatomy_heart;
    public GameObject _anatomy_vml;
    public GameObject _anatomy_irregular;
    public Mesh _heart_mesh_imported;
    public GameObject _anatomy_real_heart;
    

    [Header("Heart parameters (meters)")]
    public float _heart_radius_x = 0.03f;
    public float _heart_radius_y = 0.02f;
    public float _heart_radius_z = 0.025f;
    public int _heart_lat_segments = 24;
    public int _heart_lon_segments = 32;

    [Header("VML parameters (meters)")]
    public float _vml_width  = 0.08f;
    public float _vml_height = 0.03f;
    public float _vml_depth  = 0.04f;
    public float _vml_defect_radius   = 0.02f;
    public float _vml_defect_depth    = 0.015f;
    public int   _vml_top_resolution  = 32;

    [Header("Calvarial skull cap parameters (meters)")]
    [Tooltip("Horizontal radius of the skull cap (X and Z). ~4.5 cm gives ~9 cm across.")]
    public float _skull_radius_xz = 0.225f; // 9 cm diameter × 5
    [Tooltip("Vertical radius of the sphere the cap is cut from. Larger = flatter cap.")]
    public float _skull_radius_y  = 0.675f; // oblate: 3x flatter than xz radius × 5
    [Tooltip("Cap half-angle in degrees — controls how much of the sphere is shown.")]
    public float _skull_cap_angle = 20f;    // unchanged — angle is dimensionless
    [Tooltip("Angular resolution around the cap.")]
    public int   _skull_lon_segs  = 48;
    [Tooltip("Radial rings from pole to edge.")]
    public int   _skull_lat_segs  = 16;

    [Header("Real heart mesh (Scenario 4 — imported FBX)")]
    public float _real_heart_scale = 300f;

    void Awake()
    {
        if (_anatomy_heart == null || _anatomy_vml == null || _anatomy_irregular == null)
        {
            ClinicalScenarioManager mgr = GetComponent<ClinicalScenarioManager>();
            if (mgr != null)
            {
                if (_anatomy_heart     == null) _anatomy_heart     = mgr._anatomy_heart;
                if (_anatomy_vml       == null) _anatomy_vml       = mgr._anatomy_scenario2;
                if (_anatomy_irregular == null) _anatomy_irregular = mgr._anatomy_scenario3;
            }
        }
        GenerateAll();
    }

    [ContextMenu("GenerateAll")]
    public void GenerateAll()
    {
        if (_anatomy_heart     != null) BuildMesh(_anatomy_heart,      BuildHeartMesh());
        if (_anatomy_vml       != null) BuildMesh(_anatomy_vml,        BuildVMLMesh());
        if (_anatomy_irregular != null) BuildMesh(_anatomy_irregular,  BuildSkullCapMesh());
        if (_anatomy_real_heart != null && _heart_mesh_imported != null)
        {
            BuildMesh(_anatomy_real_heart, _heart_mesh_imported);
            _anatomy_real_heart.transform.localScale = Vector3.one * _real_heart_scale;
        }
    }

    static void BuildMesh(GameObject target, Mesh mesh)
    {
        MeshFilter mf = target.GetComponent<MeshFilter>();
        if (mf == null) mf = target.AddComponent<MeshFilter>();
        mf.mesh = mesh;

        MeshRenderer mr = target.GetComponent<MeshRenderer>();
        if (mr == null) mr = target.AddComponent<MeshRenderer>();
        if (mr.sharedMaterial == null)
            mr.sharedMaterial = new Material(Shader.Find("Standard"));

        MeshCollider mc = target.GetComponent<MeshCollider>();
        if (mc == null) mc = target.AddComponent<MeshCollider>();
        mc.sharedMesh = mesh;
        mc.convex = false;

        // Assign to the AnatomyMesh layer so SurfaceFittingManager can raycast against it.
        int anatomy_layer = LayerMask.NameToLayer("Anatomy");
        if (anatomy_layer == -1)
            Debug.LogWarning("ClinicalScenarioMeshGenerator: 'Anatomy' layer not found. " +
                "Create it in Edit > Project Settings > Tags and Layers.");
        else
            target.layer = anatomy_layer;
    }

    Mesh BuildHeartMesh()
    {
        int lat = _heart_lat_segments;
        int lon = _heart_lon_segments;

        Vector3[] verts   = new Vector3[(lat + 1) * (lon + 1)];
        Vector3[] normals = new Vector3[verts.Length];
        Vector2[] uvs     = new Vector2[verts.Length];

        for (int i = 0; i <= lat; i++)
        {
            float phi = Mathf.PI * i / lat;
            for (int j = 0; j <= lon; j++)
            {
                float theta = 2f * Mathf.PI * j / lon;
                float x = _heart_radius_x * Mathf.Sin(phi) * Mathf.Cos(theta);
                float y = _heart_radius_y * Mathf.Cos(phi);
                float z = _heart_radius_z * Mathf.Sin(phi) * Mathf.Sin(theta);
                int idx = i * (lon + 1) + j;
                verts[idx]   = new Vector3(x, y, z);
                normals[idx] = new Vector3(
                    x / (_heart_radius_x * _heart_radius_x),
                    y / (_heart_radius_y * _heart_radius_y),
                    z / (_heart_radius_z * _heart_radius_z)
                ).normalized;
                uvs[idx] = new Vector2((float)j / lon, (float)i / lat);
            }
        }

        Mesh mesh = new Mesh { name = "HeartEllipsoid" };
        mesh.vertices  = verts;
        mesh.normals   = normals;
        mesh.uv        = uvs;
        mesh.triangles = BuildSphereTopology(lat, lon);
        mesh.RecalculateBounds();
        return mesh;
    }

    static int[] BuildSphereTopology(int lat, int lon)
    {
        int[] tris = new int[lat * lon * 6];
        int t = 0;
        for (int i = 0; i < lat; i++)
            for (int j = 0; j < lon; j++)
            {
                int a = i * (lon + 1) + j;
                int b = a + (lon + 1);
                tris[t++] = a;     tris[t++] = b;     tris[t++] = a + 1;
                tris[t++] = b;     tris[t++] = b + 1; tris[t++] = a + 1;
            }
        return tris;
    }

    Mesh BuildVMLMesh()
    {
        float hw = _vml_width  / 2f;
        float hh = _vml_height;
        float hd = _vml_depth  / 2f;

        int res = _vml_top_resolution;
        int top_vert_count = (res + 1) * (res + 1);
        Vector3[] top_verts   = new Vector3[top_vert_count];
        Vector3[] top_normals = new Vector3[top_vert_count];
        Vector2[] top_uvs     = new Vector2[top_vert_count];

        for (int i = 0; i <= res; i++)
            for (int j = 0; j <= res; j++)
            {
                float u = (float)j / res;
                float v = (float)i / res;
                float x = Mathf.Lerp(-hw, hw, u);
                float z = Mathf.Lerp(-hd, hd, v);
                float r2 = (x * x + z * z) / (_vml_defect_radius * _vml_defect_radius);
                float y_offset = -_vml_defect_depth * Mathf.Exp(-r2);
                int idx = i * (res + 1) + j;
                top_verts[idx] = new Vector3(x, y_offset, z);
                top_uvs[idx]   = new Vector2(u, v);
            }

        for (int i = 0; i <= res; i++)
            for (int j = 0; j <= res; j++)
            {
                int idx = i * (res + 1) + j;
                float x  = top_verts[idx].x;
                float z  = top_verts[idx].z;
                float r2 = (x * x + z * z) / (_vml_defect_radius * _vml_defect_radius);
                float common = 2f * _vml_defect_depth / (_vml_defect_radius * _vml_defect_radius) * Mathf.Exp(-r2);
                top_normals[idx] = new Vector3(common * x, 1f, common * z).normalized;
            }

        int[] top_tris = new int[res * res * 6];
        int t = 0;
        for (int i = 0; i < res; i++)
            for (int j = 0; j < res; j++)
            {
                int a = i * (res + 1) + j;
                int b = a + (res + 1);
                top_tris[t++] = a;     top_tris[t++] = a + 1; top_tris[t++] = b;
                top_tris[t++] = a + 1; top_tris[t++] = b + 1; top_tris[t++] = b;
            }

        Vector3[] side_verts = new Vector3[]
        {
            new(-hw, 0f,  hd), new( hw, 0f,  hd), new(-hw, -hh,  hd), new( hw, -hh,  hd),
            new( hw, 0f, -hd), new(-hw, 0f, -hd), new( hw, -hh, -hd), new(-hw, -hh, -hd),
            new( hw, 0f,  hd), new( hw, 0f, -hd), new( hw, -hh,  hd), new( hw, -hh, -hd),
            new(-hw, 0f, -hd), new(-hw, 0f,  hd), new(-hw, -hh, -hd), new(-hw, -hh,  hd),
            new(-hw, -hh,  hd), new( hw, -hh,  hd), new(-hw, -hh, -hd), new( hw, -hh, -hd),
        };
        Vector3[] side_normals = new Vector3[]
        {
            Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward,
            Vector3.back,    Vector3.back,    Vector3.back,    Vector3.back,
            Vector3.right,   Vector3.right,   Vector3.right,   Vector3.right,
            Vector3.left,    Vector3.left,    Vector3.left,    Vector3.left,
            Vector3.down,    Vector3.down,    Vector3.down,    Vector3.down,
        };
        int[] side_tris = new int[5 * 6];
        for (int face = 0; face < 5; face++)
        {
            int bv = face * 4, bt = face * 6;
            side_tris[bt+0] = bv;   side_tris[bt+1] = bv+1; side_tris[bt+2] = bv+2;
            side_tris[bt+3] = bv+1; side_tris[bt+4] = bv+3; side_tris[bt+5] = bv+2;
        }

        Mesh top_mesh  = new Mesh { vertices = top_verts,  normals = top_normals, uv = top_uvs,  triangles = top_tris };
        Mesh side_mesh = new Mesh { vertices = side_verts, normals = side_normals, triangles = side_tris };

        Mesh mesh = new Mesh { name = "VMLMuscleBlock", subMeshCount = 2 };
        mesh.CombineMeshes(new CombineInstance[]
        {
            new CombineInstance { mesh = top_mesh,  transform = Matrix4x4.identity },
            new CombineInstance { mesh = side_mesh, transform = Matrix4x4.identity },
        }, mergeSubMeshes: true, useMatrices: false);
        mesh.RecalculateBounds();
        return mesh;
    }

    // =========================================================================
    // SCENARIO 2 — Calvarial Bone Defect: slightly oblate spherical cap
    //
    // Approach: sample an oblate ellipsoid (rx=rz >> ry) from the north pole
    // down to _skull_cap_angle degrees of latitude. This gives a smooth, convex,
    // mildly curved surface — anatomically representative of the outer parietal
    // skull surface. The mesh is translated so the pole sits at y=0 and the
    // rim sits below it (open downward), matching the orientation of other scenes.
    // =========================================================================

    Mesh BuildSkullCapMesh()
    {
        int lon = _skull_lon_segs;
        int lat = _skull_lat_segs;
        float cap_angle_rad = _skull_cap_angle * Mathf.Deg2Rad;

        // Vertex count: pole + (lat rings) * (lon+1)
        // We use lon+1 verts per ring to avoid seam UV issues.
        int vert_count = 1 + lat * (lon + 1);
        Vector3[] verts   = new Vector3[vert_count];
        Vector3[] normals = new Vector3[vert_count];
        Vector2[] uvs     = new Vector2[vert_count];

        float rx = _skull_radius_xz;
        float ry = _skull_radius_y;

        // Pole vertex (phi=0, top of cap).
        verts[0]   = new Vector3(0f, ry, 0f);         // will be offset below
        normals[0] = Vector3.up;
        uvs[0]     = new Vector2(0.5f, 0.5f);

        // Ring vertices: phi goes from 0 to cap_angle_rad (exclusive of pole).
        for (int ring = 1; ring <= lat; ring++)
        {
            float phi = cap_angle_rad * ring / lat;   // 0 (exclusive) → cap_angle_rad
            float sin_phi = Mathf.Sin(phi);
            float cos_phi = Mathf.Cos(phi);

            for (int seg = 0; seg <= lon; seg++)
            {
                float theta = 2f * Mathf.PI * seg / lon;
                float x = rx * sin_phi * Mathf.Cos(theta);
                float y = ry * cos_phi;
                float z = rx * sin_phi * Mathf.Sin(theta);

                int idx = 1 + (ring - 1) * (lon + 1) + seg;
                verts[idx] = new Vector3(x, y, z);

                // Ellipsoid outward normal: (x/rx², y/ry², z/rx²) normalized.
                normals[idx] = new Vector3(
                    x / (rx * rx),
                    y / (ry * ry),
                    z / (rx * rx)
                ).normalized;

                // UV: radial from center (pole=0.5,0.5; rim=edge of unit circle).
                float r_frac = (float)ring / lat;
                uvs[idx] = new Vector2(
                    0.5f + 0.5f * r_frac * Mathf.Cos(theta),
                    0.5f + 0.5f * r_frac * Mathf.Sin(theta)
                );
            }
        }

        // Translate so pole is at y=0 (top), rim hangs below.
        float pole_y = ry;
        for (int i = 0; i < vert_count; i++)
            verts[i].y -= pole_y;

        // Topology: fan around pole + quads between rings.
        int fan_tris  = lon * 3;
        int quad_tris = (lat - 1) * lon * 6;
        int[] tris = new int[fan_tris + quad_tris];
        int t = 0;

        // Fan: pole (0) to first ring.
        for (int seg = 0; seg < lon; seg++)
        {
            int curr = 1 + seg;
            int next = 1 + seg + 1;
            tris[t++] = 0;
            tris[t++] = curr;
            tris[t++] = next;
        }

        // Quads between ring r and ring r+1.
        for (int ring = 1; ring < lat; ring++)
        {
            int row_a = 1 + (ring - 1) * (lon + 1);
            int row_b = 1 +  ring      * (lon + 1);
            for (int seg = 0; seg < lon; seg++)
            {
                int a = row_a + seg;
                int b = row_a + seg + 1;
                int c = row_b + seg;
                int d = row_b + seg + 1;
                tris[t++] = a; tris[t++] = c; tris[t++] = b;
                tris[t++] = b; tris[t++] = c; tris[t++] = d;
            }
        }

        Mesh mesh = new Mesh { name = "SkullCap" };
        mesh.vertices  = verts;
        mesh.normals   = normals;
        mesh.uv        = uvs;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
        return mesh;
    }
}