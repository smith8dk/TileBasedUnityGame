using UnityEngine;

public class PickupItem : MonoBehaviour
{
    [Header("Item Data (optional)")]
    [SerializeField] private string itemName = "New Item";
    [SerializeField] private int itemID = 0;

    [Header("Inventory UI")]
    [Tooltip("Prefab of the UI element (e.g., icon) to add under the Vertical Layout Group when picked up.")]
    [SerializeField] private GameObject inventoryItemUIPrefab;

    // Remove serialized DropZone reference, rely on singleton:
    // [SerializeField] private DropZone dropZone;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!other.CompareTag("Player"))
            return;

        Debug.Log($"Player picked up: {itemName} (ID: {itemID})");

        if (inventoryItemUIPrefab != null)
        {
            if (DropZone.Instance != null)
            {
                DropZone.Instance.AddItemToInventory(inventoryItemUIPrefab);
            }
            else
            {
                Debug.LogWarning("PickupItem: DropZone.Instance is null; ensure DropZone is in scene and active.");
            }
        }
        else
        {
            Debug.LogWarning("PickupItem: inventoryItemUIPrefab not assigned.");
        }

        Destroy(gameObject);
    }
}
