using UnityEngine;

/// <summary>
/// Programmatically generates a reference sketch loop on the calvarial defect
/// anatomy (Scenario 2, irregular perimeter wound) for experiment repeatability.
///
/// The calvarial anatomy is a smooth oblate spherical cap by design. Loop
/// points are computed analytically using the same spherical-cap
/// parametrization as ClinicalScenarioMeshGenerator.BuildSkullCapMesh, so
/// every point lands exactly on the surface. No raycasting is used, since a
/// shallow dome does not need it.
///
/// Irregularity (an organic, non-circular wound outline) is added via summed
/// sine harmonics in the azimuthal angle, since the anatomy mesh itself is
/// regular and the wound shape is meant to come from the drawn loop.
///
/// Attach to the same manager GameObject as ClinicalScenarioMeshGenerator.
/// Use the Context Menu or call GenerateSketch() from code.
/// </summary>
public class CalvarialSketchGenerator : MonoBehaviour
{
    [Header("References")]
    public DrawingModeHandler _drawing_mode_handler;
    public SurfaceFittingManager _fitting_manager;
    public ClinicalScenarioMeshGenerator _mesh_generator;
    public ClinicalScenarioManager _scenario_manager;

    [Header("Sketch Parameters")]
    [Tooltip("Number of loop points to generate.")]
    public int _sketch_segments = 80;

    [Tooltip("Base loop radius as a fraction of the cap half-angle. " +
             "1.0 = traces the cap rim; less = inset toward the pole.")]
    [Range(0.1f, 0.95f)]
    public float _sketch_radius_fraction = 0.6f;

    [Tooltip("Number of sine harmonics used to make the perimeter irregular " +
             "(organic wound outline instead of a circle). 0 = perfect circle.")]
    [Range(0, 6)]
    public int _irregularity_harmonics = 3;

    [Tooltip("Max radial perturbation from harmonics, as a fraction of the base radius.")]
    [Range(0f, 0.5f)]
    public float _irregularity_amplitude = 0.15f;

    [Tooltip("Max per-point displacement applied on top of the irregular shape (meters). " +
             "0 = no noise. Point stays on the dome surface after displacement.")]
    public float _sketch_noise_amplitude = 0f;

    [Tooltip("Random seed for reproducible sketches (shape harmonics + per-point noise). " +
             "-1 = random each time.")]
    public int _sketch_noise_seed = -1;

    [ContextMenu("Generate Sketch")]
    public void GenerateSketch()
    {
        if (_fitting_manager == null || _mesh_generator == null || _scenario_manager == null
            || _drawing_mode_handler == null)
        {
            Debug.LogError("CalvarialSketchGenerator: missing reference(s). Assign in Inspector.");
            return;
        }

        if (_scenario_manager.GetActiveScenarioIndex() != 2)
        {
            Debug.LogWarning("CalvarialSketchGenerator: Calvarial scenario (index 2) is not " +
                "the active scenario. Select it in the scenario menu before generating this sketch.");
        }

        GameObject skull_go = _scenario_manager.GetActiveAnatomy();
        if (skull_go == null)
        {
            Debug.LogWarning("CalvarialSketchGenerator: no active anatomy. Select the Calvarial scenario first.");
            return;
        }

        // Force drawing mode on so the drawing UI and line renderer are active
        // and the generated sketch is actually visible. Safe to call even if
        // drawing mode is already on; it won't clear the current line renderer.
        if (!_drawing_mode_handler.IsDrawingModeEnabled())
            _drawing_mode_handler.SetDrawingMode(true);

        float rx = _mesh_generator._skull_radius_xz;
        float ry = _mesh_generator._skull_radius_y;
        float cap_angle_rad = _mesh_generator._skull_cap_angle * Mathf.Deg2Rad;

        System.Random rng = (_sketch_noise_seed < 0)
            ? new System.Random()
            : new System.Random(_sketch_noise_seed);

        // Pre-roll random phase per harmonic so the shape is fixed for this seed
        // but still closes cleanly (sine of theta is periodic over 2*PI).
        int harmonics = _irregularity_harmonics;
        float[] harmonic_phase = new float[Mathf.Max(harmonics, 1)];
        for (int k = 0; k < harmonic_phase.Length; k++)
            harmonic_phase[k] = (float)rng.NextDouble() * 2f * Mathf.PI;

        int n = _sketch_segments;
        Vector3[] points = new Vector3[n + 1]; // +1 to close the loop

        for (int i = 0; i <= n; i++)
        {
            float theta = 2f * Mathf.PI * i / n;

            // Irregular radius: base fraction plus summed sine harmonics.
            float shape_perturbation = 0f;
            for (int k = 1; k <= harmonics; k++)
                shape_perturbation += (_irregularity_amplitude / harmonics)
                                       * Mathf.Sin(k * theta + harmonic_phase[k - 1]);

            float r_frac = _sketch_radius_fraction * (1f + shape_perturbation);
            r_frac = Mathf.Clamp(r_frac, 0.02f, 0.98f);
            float phi = cap_angle_rad * r_frac;

            // Optional per-point noise, applied as small angular jitter so the
            // resulting point still lies exactly on the analytic dome surface.
            if (_sketch_noise_amplitude > 0f)
            {
                float local_radius = Mathf.Max(rx * Mathf.Sin(phi), 0.001f);
                float theta_noise = ((float)rng.NextDouble() * 2f - 1f)
                                     * (_sketch_noise_amplitude / local_radius);
                float r_frac_noise = ((float)rng.NextDouble() * 2f - 1f)
                                      * (_sketch_noise_amplitude / (rx * cap_angle_rad));

                theta += theta_noise;
                r_frac = Mathf.Clamp(r_frac + r_frac_noise, 0.02f, 0.98f);
                phi = cap_angle_rad * r_frac;
            }

            float sin_phi = Mathf.Sin(phi);
            float cos_phi = Mathf.Cos(phi);

            // Local-space point on the cap, matching BuildSkullCapMesh exactly
            // (pole translated to y=0, rim hangs below).
            float lx = rx * sin_phi * Mathf.Cos(theta);
            float ly = ry * cos_phi - ry;
            float lz = rx * sin_phi * Mathf.Sin(theta);

            points[i] = skull_go.transform.TransformPoint(new Vector3(lx, ly, lz));
        }

        LineRenderer lr = _drawing_mode_handler._line_renderer;
        _fitting_manager._drawn_loop = lr;
        lr.positionCount = n + 1;
        for (int i = 0; i <= n; i++)
            lr.SetPosition(i, points[i]);

        Debug.Log($"CalvarialSketchGenerator: generated sketch with {n + 1} points " +
                  $"at radius fraction {_sketch_radius_fraction}, " +
                  $"{harmonics} irregularity harmonics (amplitude {_irregularity_amplitude:F2}), " +
                  $"noise={_sketch_noise_amplitude * 1000f:F1} mm.");
    }
}
