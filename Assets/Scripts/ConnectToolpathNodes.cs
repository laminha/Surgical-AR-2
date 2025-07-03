using UnityEngine;

public class ConnectToolpathNodes : MonoBehaviour
{
    public LineRenderer _line_renderer;
    [ContextMenu("Update")]
    void Update()
    {
        int child_count = transform.childCount;

        _line_renderer.positionCount = child_count;
        for (int i = 0; i < child_count; i++)
        {
            Transform curr_child_transform = transform.GetChild(i);
            _line_renderer.SetPosition(i, curr_child_transform.localPosition);
        }
    }
}
