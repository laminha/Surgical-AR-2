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
/// Scenario 2 — Irregular Perimeter Wound:
///   Gently domed surface with a kidney-bean / non-convex outer boundary.
/// </summary>
/// 
[RequireComponent(typeof(ClinicalScenarioManager))]
public class ClinicalScenarioMeshGenerator : MonoBehaviour
{
    [Header("References — set by ClinicalScenarioManager or manually")]
    public GameObject _anatomy_heart;
    public GameObject _anatomy_vml;
    public GameObject _anatomy_irregular;

    [Header("Heart parameters (meters)")]
    public float _heart_radius_x = 0.03f;  // 6 cm total width
    public float _heart_radius_y = 0.02f;  // 4 cm total height (flattened)
    public float _heart_radius_z = 0.025f; // 5 cm total depth
    public int _heart_lat_segments = 24;
    public int _heart_lon_segments = 32;

    [Header("VML parameters (meters)")]
    public float _vml_width  = 0.08f;  // 8 cm
    public float _vml_height = 0.03f;  // 3 cm
    public float _vml_depth  = 0.04f;  // 4 cm
    public float _vml_defect_radius   = 0.02f;  // Gaussian defect radius
    public float _vml_defect_depth    = 0.015f; // Defect depth into the block
    public int   _vml_top_resolution  = 32;     // Quads per side on top face

    [Header("Irregular perimeter parameters (meters)")]
    public float _irr_base_radius  = 0.03f;  // Mean radius ~3 cm
    public float _irr_dome_height  = 0.008f; // Gentle dome ~8 mm
    public int   _irr_radial_segs  = 64;     // Angular resolution
    public int   _irr_ring_segs    = 16;     // Radial rings

    void Awake()
    {
        // Pull references from ClinicalScenarioManager if not set manually.
        if (_anatomy_heart == null || _anatomy_vml == null || _anatomy_irregular == null)
        {
            ClinicalScenarioManager mgr = GetComponent<ClinicalScenarioManager>();
            if (mgr != null)
            {
                if (_anatomy_heart    == null) _anatomy_heart    = mgr._anatomy_heart;
                if (_anatomy_vml      == null) _anatomy_vml      = mgr._anatomy_scenario2;
                if (_anatomy_irregular== null) _anatomy_irregular= mgr._anatomy_scenario3;
            }
        }
        GenerateAll();
    }

    // -------------------------------------------------------------------------
    // Public entry point
    // -------------------------------------------------------------------------

    [ContextMenu("GenerateAll")]
    public void GenerateAll()
    {
        if (_anatomy_heart     != null) BuildMesh(_anatomy_heart,     BuildHeartMesh());
        if (_anatomy_vml       != null) BuildMesh(_anatomy_vml,       BuildVMLMesh());
        if (_anatomy_irregular != null) BuildMesh(_anatomy_irregular, BuildIrregularMesh());
    }

    // -------------------------------------------------------------------------
    // Shared helper — assign mesh + collider to a GameObject
    // -------------------------------------------------------------------------

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
    }

    // =========================================================================
    // SCENARIO 0 — Heart: oblate ellipsoid
    // =========================================================================

    Mesh BuildHeartMesh()
    {
        int lat = _heart_lat_segments;
        int lon = _heart_lon_segments;

        Vector3[] verts = new Vector3[(lat + 1) * (lon + 1)];
        Vector3[] normals = new Vector3[verts.Length];
        Vector2[] uvs = new Vector2[verts.Length];

        for (int i = 0; i <= lat; i++)
        {
            float phi = Mathf.PI * i / lat;          // 0 → π (pole to pole)
            for (int j = 0; j <= lon; j++)
            {
                float theta = 2f * Mathf.PI * j / lon; // 0 → 2π

                float x = _heart_radius_x * Mathf.Sin(phi) * Mathf.Cos(theta);
                float y = _heart_radius_y * Mathf.Cos(phi);
                float z = _heart_radius_z * Mathf.Sin(phi) * Mathf.Sin(theta);

                int idx = i * (lon + 1) + j;
                verts[idx]   = new Vector3(x, y, z);
                // Normal for ellipsoid: divide each component by radius² then normalize.
                normals[idx] = new Vector3(
                    x / (_heart_radius_x * _heart_radius_x),
                    y / (_heart_radius_y * _heart_radius_y),
                    z / (_heart_radius_z * _heart_radius_z)
                ).normalized;
                uvs[idx] = new Vector2((float)j / lon, (float)i / lat);
            }
        }

        int[] tris = BuildSphereTopology(lat, lon);

        Mesh mesh = new Mesh { name = "HeartEllipsoid" };
        mesh.vertices  = verts;
        mesh.normals   = normals;
        mesh.uv        = uvs;
        mesh.triangles = tris;
        mesh.RecalculateBounds();
        return mesh;
    }

    // Shared topology builder for sphere-like grids.
    static int[] BuildSphereTopology(int lat, int lon)
    {
        int[] tris = new int[lat * lon * 6];
        int t = 0;
        for (int i = 0; i < lat; i++)
        {
            for (int j = 0; j < lon; j++)
            {
                int a = i * (lon + 1) + j;
                int b = a + (lon + 1);
                tris[t++] = a;     tris[t++] = b;     tris[t++] = a + 1;
                tris[t++] = b;     tris[t++] = b + 1; tris[t++] = a + 1;
            }
        }
        return tris;
    }

    // =========================================================================
    // SCENARIO 1 — Volumetric Muscle Loss: box + gaussian defect on top face
    // =========================================================================

    Mesh BuildVMLMesh()
    {
        // We build the 5 solid faces of a box (no bottom) plus a subdivided top face
        // that has a gaussian-shaped depression pressed into it.

        float hw = _vml_width  / 2f;
        float hh = _vml_height;       // y goes from 0 (top, where defect is) to -hh (base)
        float hd = _vml_depth  / 2f;

        // --- Top face (subdivided, with defect) ---
        int res = _vml_top_resolution;
        int top_vert_count = (res + 1) * (res + 1);
        Vector3[] top_verts   = new Vector3[top_vert_count];
        Vector3[] top_normals = new Vector3[top_vert_count];
        Vector2[] top_uvs     = new Vector2[top_vert_count];

        for (int i = 0; i <= res; i++)
        {
            for (int j = 0; j <= res; j++)
            {
                float u = (float)j / res; // 0→1 along X
                float v = (float)i / res; // 0→1 along Z
                float x = Mathf.Lerp(-hw, hw, u);
                float z = Mathf.Lerp(-hd, hd, v);

                // Gaussian depression centered at (0, 0) on the top face.
                float r2 = (x * x + z * z) / (_vml_defect_radius * _vml_defect_radius);
                float y_offset = -_vml_defect_depth * Mathf.Exp(-r2);

                int idx = i * (res + 1) + j;
                top_verts[idx]   = new Vector3(x, y_offset, z);
                top_uvs[idx]     = new Vector2(u, v);
            }
        }
        // Recalculate normals analytically for the gaussian surface.
        for (int i = 0; i <= res; i++)
        {
            for (int j = 0; j <= res; j++)
            {
                int idx = i * (res + 1) + j;
                float x = top_verts[idx].x;
                float z = top_verts[idx].z;
                float r2 = (x * x + z * z) / (_vml_defect_radius * _vml_defect_radius);
                float common = 2f * _vml_defect_depth / (_vml_defect_radius * _vml_defect_radius) * Mathf.Exp(-r2);
                // Gradient of gaussian: dy/dx = common * x,  dy/dz = common * z
                // Normal = (-dy/dx, 1, -dy/dz) normalized.
                top_normals[idx] = new Vector3(common * x, 1f, common * z).normalized;
            }
        }

        int[] top_tris = new int[res * res * 6];
        int t = 0;
        for (int i = 0; i < res; i++)
        {
            for (int j = 0; j < res; j++)
            {
                int a = i * (res + 1) + j;
                int b = a + (res + 1);
                top_tris[t++] = a;     top_tris[t++] = a + 1; top_tris[t++] = b;
                top_tris[t++] = a + 1; top_tris[t++] = b + 1; top_tris[t++] = b;
            }
        }

        // --- 4 side faces + bottom (simple quads, no subdivision needed) ---
        // Each face: 4 verts, 2 tris.
        // Order: front (+z), back (-z), right (+x), left (-x), bottom (-y).
        Vector3[] side_verts = new Vector3[]
        {
            // Front face (+z)
            new(-hw,   0f,  hd), new( hw,   0f,  hd),
            new(-hw, -hh,  hd), new( hw, -hh,  hd),
            // Back face (-z)
            new( hw,   0f, -hd), new(-hw,   0f, -hd),
            new( hw, -hh, -hd), new(-hw, -hh, -hd),
            // Right face (+x)
            new( hw,   0f,  hd), new( hw,   0f, -hd),
            new( hw, -hh,  hd), new( hw, -hh, -hd),
            // Left face (-x)
            new(-hw,   0f, -hd), new(-hw,   0f,  hd),
            new(-hw, -hh, -hd), new(-hw, -hh,  hd),
            // Bottom face (-y)
            new(-hw, -hh,  hd), new( hw, -hh,  hd),
            new(-hw, -hh, -hd), new( hw, -hh, -hd),
        };

        Vector3[] side_normals = new Vector3[]
        {
            // Front
            Vector3.forward, Vector3.forward, Vector3.forward, Vector3.forward,
            // Back
            Vector3.back, Vector3.back, Vector3.back, Vector3.back,
            // Right
            Vector3.right, Vector3.right, Vector3.right, Vector3.right,
            // Left
            Vector3.left, Vector3.left, Vector3.left, Vector3.left,
            // Bottom
            Vector3.down, Vector3.down, Vector3.down, Vector3.down,
        };

        int[] side_tris = new int[5 * 6];
        for (int face = 0; face < 5; face++)
        {
            int base_v = face * 4;
            int base_t = face * 6;
            side_tris[base_t + 0] = base_v;     side_tris[base_t + 1] = base_v + 1; side_tris[base_t + 2] = base_v + 2;
            side_tris[base_t + 3] = base_v + 1; side_tris[base_t + 4] = base_v + 3; side_tris[base_t + 5] = base_v + 2;
        }

        // --- Combine top and side sub-meshes ---
        Mesh mesh = new Mesh { name = "VMLMuscleBlock" };
        mesh.subMeshCount = 2;

        CombineInstance[] combine = new CombineInstance[2];

        Mesh top_mesh = new Mesh();
        top_mesh.vertices  = top_verts;
        top_mesh.normals   = top_normals;
        top_mesh.uv        = top_uvs;
        top_mesh.triangles = top_tris;

        Mesh side_mesh = new Mesh();
        side_mesh.vertices  = side_verts;
        side_mesh.normals   = side_normals;
        side_mesh.triangles = side_tris;

        combine[0] = new CombineInstance { mesh = top_mesh,  transform = Matrix4x4.identity };
        combine[1] = new CombineInstance { mesh = side_mesh, transform = Matrix4x4.identity };

        mesh.CombineMeshes(combine, mergeSubMeshes: true, useMatrices: false);
        mesh.RecalculateBounds();
        return mesh;
    }

    // =========================================================================
    // SCENARIO 2 — Irregular perimeter: domed surface, kidney-bean boundary
    // =========================================================================

    Mesh BuildIrregularMesh()
    {
        // Strategy: polar grid where the outer radius varies with angle
        // to produce a non-convex kidney-bean perimeter.
        // Surface height follows a gaussian dome from center.

        int rad_segs = _irr_ring_segs;
        int ang_segs = _irr_radial_segs;

        // r(theta) = base_radius * (1 + A*cos + B*sin + ...) — Fourier terms for kidney shape.
        // These coefficients produce a clear non-elliptical, non-convex boundary.
        float R  = _irr_base_radius;

        // Returns the boundary radius at a given angle.
        float BoundaryRadius(float theta)
        {
            return R * (
                1.0f
                + 0.25f * Mathf.Cos(theta)          // elongate slightly
                - 0.20f * Mathf.Cos(2f * theta)     // flatten one side
                + 0.10f * Mathf.Sin(3f * theta)     // add lobes
                - 0.08f * Mathf.Cos(4f * theta)     // fine irregularity
            );
        }

        // Returns the dome height at a given radius fraction (0=center, 1=boundary).
        float DomeHeight(float r_frac)
        {
            return _irr_dome_height * Mathf.Exp(-3f * r_frac * r_frac);
        }

        int vert_count = ang_segs * (rad_segs + 1) + 1; // rings + center point
        Vector3[] verts   = new Vector3[vert_count];
        Vector3[] normals = new Vector3[vert_count];
        Vector2[] uvs     = new Vector2[vert_count];

        // Center vertex.
        verts[0]   = new Vector3(0f, DomeHeight(0f), 0f);
        normals[0] = Vector3.up;
        uvs[0]     = new Vector2(0.5f, 0.5f);

        // Ring vertices.
        for (int ring = 0; ring <= rad_segs; ring++)
        {
            float r_frac = (float)(ring + 1) / (rad_segs + 1); // 0 exclusive → 1 inclusive
            for (int seg = 0; seg < ang_segs; seg++)
            {
                float theta = 2f * Mathf.PI * seg / ang_segs;
                float boundary_r = BoundaryRadius(theta);
                float r = r_frac * boundary_r;
                float x = r * Mathf.Cos(theta);
                float z = r * Mathf.Sin(theta);
                float y = DomeHeight(r_frac);

                int idx = 1 + ring * ang_segs + seg;
                verts[idx] = new Vector3(x, y, z);
                uvs[idx]   = new Vector2(
                    0.5f + 0.5f * r_frac * Mathf.Cos(theta),
                    0.5f + 0.5f * r_frac * Mathf.Sin(theta)
                );
            }
        }

        // Normals — recalculate after mesh is built.
        // (Analytical normals for this shape are complex; RecalculateNormals handles it fine.)

        // Topology.
        // Inner fan: center → first ring.
        int fan_count = ang_segs * 3;
        // Quads between rings.
        int quad_count = rad_segs * ang_segs * 6;
        int[] tris = new int[fan_count + quad_count];
        int t = 0;

        // Fan around center.
        for (int seg = 0; seg < ang_segs; seg++)
        {
            int curr = 1 + seg;
            int next = 1 + (seg + 1) % ang_segs;
            tris[t++] = 0;
            tris[t++] = curr;
            tris[t++] = next;
        }

        // Quads between consecutive rings.
        for (int ring = 0; ring < rad_segs; ring++)
        {
            int ring_start      = 1 + ring * ang_segs;
            int next_ring_start = 1 + (ring + 1) * ang_segs;
            for (int seg = 0; seg < ang_segs; seg++)
            {
                int a = ring_start      + seg;
                int b = ring_start      + (seg + 1) % ang_segs;
                int c = next_ring_start + seg;
                int d = next_ring_start + (seg + 1) % ang_segs;
                tris[t++] = a; tris[t++] = c; tris[t++] = b;
                tris[t++] = b; tris[t++] = c; tris[t++] = d;
            }
        }

        Mesh mesh = new Mesh { name = "IrregularPerimeterWound" };
        mesh.vertices  = verts;
        mesh.uv        = uvs;
        mesh.triangles = tris;
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        return mesh;
    }
}
