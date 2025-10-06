using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System.Text.RegularExpressions;

public class DropZone : MonoBehaviour, IDropHandler, IPointerEnterHandler, IPointerExitHandler
{
    // Singleton instance
    public static DropZone Instance { get; private set; }

    private Image dropZoneImage;

    [Header("Optional: Parent for instantiated UI items")]
    [Tooltip("If null, instantiated UI items will be parented to this GameObject's transform.")]
    [SerializeField] private Transform contentParent;

    private void Awake()
    {
        // Setup singleton instance
        if (Instance != null && Instance != this)
        {
            Debug.LogWarning("Multiple DropZone instances detected. Destroying duplicate on " + gameObject.name);
            Destroy(this);
            return;
        }
        Instance = this;
    }

    private void OnDestroy()
    {
        if (Instance == this)
            Instance = null;
    }

    private void Start()
    {
        dropZoneImage = GetComponent<Image>();
        if (contentParent == null)
            contentParent = this.transform;
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        // (optional feedback)
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        // (optional feedback)
    }

    public void OnDrop(PointerEventData eventData)
    {
        Debug.Log("Dropped into DropZone");

        GameObject dropped = eventData.pointerDrag;
        if (dropped == null) return;

        var draggableItem = dropped.GetComponent<Draggable>();
        if (draggableItem != null)
        {
            draggableItem.parentAfterDrag = transform;
        }

        // Disable collisions or other components on the dropped object if needed
        Collider2D col2d = dropped.GetComponent<Collider2D>();
        if (col2d != null) col2d.enabled = false;
        Collider col3d = dropped.GetComponent<Collider>();
        if (col3d != null) col3d.enabled = false;
        EventTrigger evt = dropped.GetComponent<EventTrigger>();
        if (evt != null) evt.enabled = false;
    }

    /// <summary>
    /// Instantiate a UI prefab under the Vertical Layout Group (contentParent).
    /// Can be called via DropZone.Instance.AddItemToInventory(...)
    /// </summary>
    public void AddItemToInventory(GameObject itemUIPrefab)
    {
        if (itemUIPrefab == null)
        {
            Debug.LogWarning("DropZone.AddItemToInventory: prefab is null.");
            return;
        }
        if (contentParent == null)
        {
            Debug.LogWarning("DropZone.AddItemToInventory: contentParent is null.");
            return;
        }

        // Instantiate under contentParent, preserving local transform:
        GameObject uiItem = Instantiate(itemUIPrefab, contentParent, false);

        // Clean up name: remove "(Clone)" or trailing numbers if any
        string baseName = itemUIPrefab.name;
        // Remove any trailing "(Clone)" just in case
        if (baseName.EndsWith("(Clone)"))
        {
            baseName = baseName.Substring(0, baseName.Length - "(Clone)".Length).Trim();
        }
        // Strip any trailing numeric suffix, with or without parentheses:
        // Matches: "Item (1)" or "Item 1" etc.
        baseName = Regex.Replace(baseName, @"\s*(?:\(\d+\)|\d+)$", "").TrimEnd();
        uiItem.name = baseName;

        // Ensure localScale matches prefab’s
        uiItem.transform.localScale = itemUIPrefab.transform.localScale;
        uiItem.transform.localRotation = Quaternion.identity;

        // Place on top of siblings so it draws above earlier items
        uiItem.transform.SetAsLastSibling();
    }
}
