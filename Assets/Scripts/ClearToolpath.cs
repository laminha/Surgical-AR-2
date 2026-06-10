using Oculus.Interaction;
using UnityEngine;
using TMPro;
using System.Collections;

public class ClearToolpath : MonoBehaviour
{
    public BSurfaceGcodeGenerator _gcode_generator;
    public TextMeshPro _button_text;
    public string _default_text = "Clear\nToolpath";
    public float _feedback_duration = 1f;

    void Start()
    {
        RayInteractable ray = GetComponent<RayInteractable>();
        ray.WhenSelectingInteractorViewAdded += Selected;
    }

    void Selected(IInteractorView arg)
    {
        _gcode_generator.ClearToolpath();
        StartCoroutine(ShowClearFeedback());
    }

    IEnumerator ShowClearFeedback()
    {
        _button_text.text = "Clearing...";
        _button_text.color = Color.green;
        yield return new WaitForSeconds(_feedback_duration);
        _button_text.text = _default_text;
        _button_text.color = Color.white;
    }
}