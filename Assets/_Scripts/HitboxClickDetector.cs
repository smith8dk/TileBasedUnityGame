using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class HitboxClickDetector : MonoBehaviour
{
    /// <summary>
    /// Fired when the user clicks on this collider.
    /// </summary>
    public System.Action onClicked;

    private Collider2D col;

    void Awake()
    {
        col = GetComponent<Collider2D>();
        col.isTrigger = true; // keep it a trigger
    }

    void Update()
    {
        // Only check on press
        if (!Input.GetMouseButtonDown(0))
            return;

        // Convert mouse to world (z=0 plane)
        Vector3 wp = Camera.main.ScreenToWorldPoint(Input.mousePosition);
        wp.z = col.gameObject.transform.position.z;

        // If the click point is inside our collider bounds...
        if (col.OverlapPoint(wp))
        {
            onClicked?.Invoke();
        }
    }
}
