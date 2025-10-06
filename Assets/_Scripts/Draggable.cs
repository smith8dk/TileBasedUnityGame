using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System;
using System.Collections;

/// <summary>
/// Draggable with name-based blacklist: if dropped onto a GameObject (or any parent) whose name
/// matches an entry in the blacklist, the dragged item returns to its original slot.
/// This version robustly enforces parent restoration after other drop handlers run by
/// finalizing the drop in a coroutine a frame or two later.
/// </summary>
public class Draggable : MonoBehaviour, IDragHandler, IEndDragHandler, IBeginDragHandler
{
    public Image image;
    [HideInInspector]
    public Transform parentAfterDrag;

    private CanvasGroup canvasGroup;

    // Event triggered when an item is dragged or dropped
    public static event Action OnItemDraggedOrDropped;

    [Header("Blacklist (GameObject names)")]
    [Tooltip("If dropped onto an object (or any of its parents) with one of these names (case-insensitive), the item will return to its original slot.")]
    [SerializeField] private string[] blacklistedObjectNames = new string[0];

    // stored at begin drag so we can restore if needed
    private Transform originalParent;
    private Vector3 originalLocalPosition;
    private Vector2 originalAnchoredPosition;
    private int originalSiblingIndex;
    private bool hadRectTransform;

    // coroutine handle so we don't run multiple finalize coroutines at once
    private Coroutine finalizeCoroutine;

    private void Awake()
    {
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null)
        {
            canvasGroup = gameObject.AddComponent<CanvasGroup>();
        }
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        // Save original parent/position so we can restore on blacklist
        originalParent = transform.parent;
        originalSiblingIndex = transform.GetSiblingIndex();

        // Save position depending on whether this has a RectTransform (UI)
        var rt = GetComponent<RectTransform>();
        if (rt != null)
        {
            hadRectTransform = true;
            originalAnchoredPosition = rt.anchoredPosition;
        }
        else
        {
            hadRectTransform = false;
            originalLocalPosition = transform.localPosition;
        }

        // parentAfterDrag is left to be possibly updated by InventorySlot.OnDrop
        parentAfterDrag = transform.parent;

        // Move to root for dragging so it appears on top of other UI
        transform.SetParent(transform.root);
        transform.SetAsLastSibling();
        if (image != null) image.raycastTarget = false;

        canvasGroup.blocksRaycasts = false; // Disable raycast blocking

        // Re-enable Image or SpriteRenderer if disabled
        if (image != null && !image.enabled)
        {
            image.enabled = true;
        }

        SpriteRenderer spriteRenderer = GetComponent<SpriteRenderer>();
        if (spriteRenderer != null && !spriteRenderer.enabled)
        {
            spriteRenderer.enabled = true;
        }
    }

    public void OnDrag(PointerEventData eventData)
    {
        transform.position = Input.mousePosition;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        // Determine the immediate drop target from the pointer event (prefer the raycast result's gameObject)
        GameObject dropTarget = null;
        if (eventData != null)
        {
            if (eventData.pointerCurrentRaycast.gameObject != null)
                dropTarget = eventData.pointerCurrentRaycast.gameObject;
            else if (eventData.pointerEnter != null)
                dropTarget = eventData.pointerEnter;
        }

        // If the drop target (or any of its parents) matches a blacklisted name, force return to original parent
        if (IsBlacklistedByName(dropTarget))
        {
            // force parentAfterDrag to originalParent so the usual restore goes back
            parentAfterDrag = originalParent;
        }

        // Immediately set parent to the parentAfterDrag (InventorySlot may have set this earlier).
        // We still finalize after other handlers run to ensure no other code re-parents us back onto a blacklisted object.
        if (image != null) image.raycastTarget = true;
        transform.SetParent(parentAfterDrag);

        // Start the finalize coroutine to check the final hierarchy after other drop handlers/layout settle
        if (finalizeCoroutine != null)
            StopCoroutine(finalizeCoroutine);
        finalizeCoroutine = StartCoroutine(FinalizeDropCoroutine());
    }

    /// <summary>
    /// Wait a couple frames so other OnDrop handlers and layout rebuilds can finish,
    /// then ensure the object is not parented under any blacklisted object. If it is,
    /// reparent to originalParent and restore UI position/sibling index. Finally, raise the event.
    /// </summary>
    private IEnumerator FinalizeDropCoroutine()
    {
        // Wait two frames to be robust against other handlers and layout code
        yield return null;
        yield return null;

        // If current parent (or any ancestor) is blacklisted, force return to originalParent
        bool parentIsBlacklisted = false;
        Transform currentParent = transform.parent;
        if (currentParent != null)
        {
            parentIsBlacklisted = IsBlacklistedByName(currentParent.gameObject);
        }

        if (parentIsBlacklisted)
        {
            // Force the parent back
            transform.SetParent(originalParent);

            // Restore correct UI/local transform
            var rt = GetComponent<RectTransform>();
            if (hadRectTransform && rt != null)
            {
                rt.anchoredPosition = originalAnchoredPosition;
            }
            else
            {
                transform.localPosition = originalLocalPosition;
            }

            // Restore sibling index if possible
            try
            {
                transform.SetSiblingIndex(originalSiblingIndex);
            }
            catch { /* ignore if layout group overrides it */ }
        }
        else
        {
            // If not blacklisted, ensure layout is stable (optionally re-apply anchored/local positions if needed)
            // No forced changes here; InventorySlot's parenting is respected.
        }

        // Re-enable raycasts/blocks (make sure UI interaction works again)
        canvasGroup.blocksRaycasts = true;

        // Fire the drag/drop event now that the final state is established
        OnItemDraggedOrDropped?.Invoke();
        Debug.Log("OnItemDraggedOrDropped event triggered (finalized).");

        finalizeCoroutine = null;
    }

    /// <summary>
    /// Returns true if the given GameObject or any of its parents has a name that is in the blacklist.
    /// A null target is considered not blacklisted.
    /// </summary>
    private bool IsBlacklistedByName(GameObject target)
    {
        if (target == null || blacklistedObjectNames == null || blacklistedObjectNames.Length == 0)
            return false;

        Transform t = target.transform;
        while (t != null)
        {
            string targetName = t.gameObject.name;
            foreach (var blackName in blacklistedObjectNames)
            {
                if (string.IsNullOrEmpty(blackName)) continue;
                if (string.Equals(targetName, blackName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            t = t.parent;
        }

        return false;
    }
}
