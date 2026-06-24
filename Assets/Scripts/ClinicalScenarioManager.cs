using UnityEngine;

/// <summary>
/// Manages the three clinical scenario anatomy GameObjects.
/// Handles activation/deactivation when a scenario is selected or cleared.
/// Attach to a manager GameObject alongside ClinicalScenarioMeshGenerator.
/// </summary>
public class ClinicalScenarioManager : MonoBehaviour
{
    [Header("Anatomy GameObjects (assign in Inspector)")]
    public GameObject _anatomy_heart;
    public GameObject _anatomy_scenario2; // Volumetric Muscle Loss
    public GameObject _anatomy_scenario3; // Irregular Perimeter Wound
    public GameObject _anatomy_scenario4; // Real Heart Mesh

    [Header("Submenu Panel")]
    public GameObject _scenario_menu_panel;

    GameObject _active_anatomy = null;

    void Start()
    {
        SetAllInactive();
        _scenario_menu_panel.SetActive(false);
    }

    public void ToggleScenarioMenu()
    {
        _scenario_menu_panel.SetActive(!_scenario_menu_panel.activeSelf);
    }

    /// <summary>
    /// Activates the requested scenario anatomy and deactivates all others.
    /// scenario_index: 0=Heart, 1=VML, 2=Irregular, -1=Clear
    /// </summary>
    int _active_scenario_index = -1;
    public void SelectScenario(int scenario_index)
    {
        SetAllInactive();
        _active_scenario_index = scenario_index;
        switch (scenario_index)
        {
            case 0: Activate(_anatomy_heart);      break;
            case 1: Activate(_anatomy_scenario2);  break;
            case 2: Activate(_anatomy_scenario3);  break;
            case 3: Activate(_anatomy_scenario4);  break;
        }
        // Panel stays open.
    }
    public int GetActiveScenarioIndex() => _active_scenario_index;

    void Activate(GameObject anatomy)
    {
        if (anatomy == null)
        {
            Debug.LogWarning("ClinicalScenarioManager: anatomy GameObject is not assigned.");
            return;
        }
        anatomy.SetActive(true);
        _active_anatomy = anatomy;
    }

    void SetAllInactive()
    {
        if (_anatomy_heart     != null) _anatomy_heart.SetActive(false);
        if (_anatomy_scenario2 != null) _anatomy_scenario2.SetActive(false);
        if (_anatomy_scenario3 != null) _anatomy_scenario3.SetActive(false);
        if (_anatomy_scenario4 != null) _anatomy_scenario4.SetActive(false);
        _active_anatomy = null;
        _active_scenario_index = -1;
    }

    /// <summary>
    /// Returns the currently active anatomy GameObject, or null if cleared.
    /// Used by SurfaceFittingManager for anatomy-aware projection.
    /// </summary>
    public GameObject GetActiveAnatomy() => _active_anatomy;

    /// <summary>
    /// Returns the world-space centroid of the active anatomy's mesh bounds.
    /// Falls back to transform.position if no MeshFilter is present.
    /// </summary>
    public Vector3 GetAnatomyCentroid()
    {
        if (_active_anatomy == null)
            return Vector3.zero;

        MeshFilter mf = _active_anatomy.GetComponent<MeshFilter>();
        if (mf != null && mf.mesh != null)
            return _active_anatomy.transform.TransformPoint(mf.mesh.bounds.center);

        return _active_anatomy.transform.position;
    }
}