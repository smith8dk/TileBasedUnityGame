using UnityEngine;
using UnityEngine.UI;

public class DisableChildComponents : MonoBehaviour
{
    // Start is called before the first frame update
    void Start()
    {
        // Disable components of existing children
        DisableComponentsInChildren();
    }

    // Update is called once per frame
    void Update()
    {
        // Continuously check for new children added during runtime
        DisableComponentsInChildren();
    }

    void DisableComponentsInChildren()
    {
        // Get all child objects and check for SpriteRenderer and Image components
        foreach (Transform child in transform)
        {
            SpriteRenderer spriteRenderer = child.GetComponent<SpriteRenderer>();
            if (spriteRenderer != null)
            {
                spriteRenderer.enabled = false; // Disable SpriteRenderer
            }

            Image image = child.GetComponent<Image>();
            if (image != null)
            {
                image.enabled = false; // Disable Image
            }
        }
    }
}
