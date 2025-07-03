using UnityEngine;

public class Follow : MonoBehaviour
{
    public GameObject _object_to_follow;
    public Vector3 _displacement;
    // Start is called once before the first execution of Update after the MonoBehaviour is created
    void Start()
    {
        
    }

    // Update is called once per frame
    void Update()
    {
        transform.rotation = _object_to_follow.transform.rotation;
        transform.position = _object_to_follow.transform.position;
        transform.Translate(_displacement);
    }
}
