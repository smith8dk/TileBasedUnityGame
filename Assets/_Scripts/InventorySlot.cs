using UnityEngine;
using UnityEngine.EventSystems;
using System;
using System.Collections.Generic;

/// <summary>
/// InventorySlot that prefers physics (collider) detection on the slot GameObject itself.
/// Put a BoxCollider2D (recommended) or BoxCollider on the same GameObject as the InventorySlot
/// (or an ancestor). Enable 'usePhysicsRaycast' and the system will prefer physics hits.
/// 
/// Notes:
/// - For UI: prefer Canvas Render Mode = Screen Space - Camera (assign Render Camera) or World Space.
/// - Add Physics2DRaycaster (2D) or PhysicsRaycaster (3D) to the camera if you want EventSystem to include physics hits.
/// - Use a dedicated layer for slot colliders and set physicsLayerMask accordingly for better performance.
/// </summary>
public class InventorySlot : MonoBehaviour, IDropHandler, IPointerDownHandler, IPointerUpHandler
{
    [SerializeField] private int InventorySize = 1;
    [SerializeField] private float holdTime = 1f; // Time to hold down before starting drag

    [Header("Physics raycast options")]
    [Tooltip("If true, prefer physics (collider) hits to identify drop targets.")]
    public bool usePhysicsRaycast = true;
    [Tooltip("If true, use 2D physics (OverlapPointAll). If false, use 3D physics (RaycastAll).")]
    public bool use2DPhysics = true;
    [Tooltip("Layer mask used for physics raycasts. Use a dedicated 'Slots' layer to avoid unrelated colliders.")]
    public LayerMask physicsLayerMask = ~0; // default all layers

    private float holdTimer = 0f;
    private bool isHolding = false;
    private bool isDragging = false;
    private Transform childToDrag;
    private Draggable draggable;
    private CanvasGroup canvasGroup;

    // Event triggered when an item is dragged or dropped (mirrors your existing pattern)
    public static event Action OnItemDraggedOrDropped;

    private void Update()
    {
        // Hold to drag logic
        if (isHolding)
        {
            holdTimer += Time.deltaTime;
            if (holdTimer >= holdTime)
            {
                isHolding = false;
                StartDraggingFirstChild(); // Initiate dragging when the hold time is exceeded
            }
        }

        // While dragging, keep object following cursor and call draggable.OnDrag for consistency
        if (isDragging && childToDrag != null)
        {
            childToDrag.position = Input.mousePosition;
            PointerEventData pointerData = new PointerEventData(EventSystem.current)
            {
                position = Input.mousePosition
            };
            if (draggable != null)
                draggable.OnDrag(pointerData);
        }
    }

    public void OnDrop(PointerEventData eventData)
    {
        GameObject droppedObject = eventData.pointerDrag;
        if (droppedObject != null)
        {
            if (transform.childCount < InventorySize)
            {
                Draggable draggableItem = droppedObject.GetComponent<Draggable>();
                if (draggableItem != null)
                {
                    draggableItem.parentAfterDrag = transform;
                    Debug.Log("[InventorySlot] Dropped on InventorySlot: " + name);

                    OnItemDraggedOrDropped?.Invoke();
                }
            }
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left && transform.childCount > 0)
        {
            isHolding = true;
            holdTimer = 0f;
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            isHolding = false;
            if (isDragging)
            {
                isDragging = false;
                EndDragging();
            }
        }
    }

    private void StartDraggingFirstChild()
    {
        if (transform.childCount > 0)
        {
            childToDrag = transform.GetChild(0);
            draggable = childToDrag.GetComponent<Draggable>();

            if (draggable != null)
            {
                if (draggable.GetComponent<CanvasGroup>() == null)
                    draggable.gameObject.AddComponent<CanvasGroup>();

                canvasGroup = draggable.GetComponent<CanvasGroup>();

                // Disable blocking so pointer can hit slots under the dragged object
                canvasGroup.blocksRaycasts = false;

                // Fire OnBeginDrag so other handlers run
                PointerEventData pointerEventData = new PointerEventData(EventSystem.current);
                ExecuteEvents.Execute(draggable.gameObject, pointerEventData, ExecuteEvents.beginDragHandler);

                isDragging = true;
                OnItemDraggedOrDropped?.Invoke();
            }
        }
    }

    private void EndDragging()
    {
        if (childToDrag != null)
        {
            if (draggable != null)
            {
                // Re-enable blocking after drop
                canvasGroup.blocksRaycasts = true;

                // Trigger OnEndDrag for the draggable
                PointerEventData pointerEventData = new PointerEventData(EventSystem.current)
                {
                    position = Input.mousePosition
                };
                ExecuteEvents.Execute(draggable.gameObject, pointerEventData, ExecuteEvents.endDragHandler);

                // Find drop target using preferred method (physics first if enabled)
                GameObject dropTarget = GetDropTargetUnderPointer();
                if (dropTarget != null)
                {
                    // Parent the dragged object to the drop target (slot)
                    childToDrag.SetParent(dropTarget.transform);
                    Debug.Log("[InventorySlot] Dropped on: " + dropTarget.name);
                }
                else
                {
                    Debug.Log("[InventorySlot] No drop target found under pointer.");
                }
            }

            OnItemDraggedOrDropped?.Invoke();

            childToDrag = null;
            draggable = null;
            canvasGroup = null;
        }
    }

    /// <summary>
    /// Find the drop target under the pointer. Prefer physics-based detection (colliders on slots)
    /// when usePhysicsRaycast is true. Otherwise, fall back to EventSystem raycasts that also check parents.
    /// Skips hits that belong to the currently dragged object so it doesn't block detection.
    /// Returns the GameObject that has the InventorySlot or DropZone component (found via GetComponentInParent).
    /// </summary>
    private GameObject GetDropTargetUnderPointer()
    {
        Vector2 screenPos = Input.mousePosition;
        Camera cam = Camera.main;

        // 1) Try physics detection if enabled
        if (usePhysicsRaycast)
        {
            var physicsHit = PhysicsRaycastAtPointer(screenPos, cam);
            if (physicsHit != null)
                return physicsHit;
        }

        // 2) Fall back to EventSystem raycast (includes UI and physics raycasters attached to camera)
        if (EventSystem.current != null)
        {
            PointerEventData pointerEventData = new PointerEventData(EventSystem.current) { position = screenPos };
            var raycastResults = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointerEventData, raycastResults);

            foreach (var result in raycastResults)
            {
                if (result.gameObject == null) continue;

                // skip if this hit is the dragged object or its children
                if (childToDrag != null && result.gameObject.transform.IsChildOf(childToDrag))
                    continue;

                // check for DropZone component in hit or parent
                var dz = result.gameObject.GetComponentInParent<DropZone>();
                if (dz != null)
                    return dz.gameObject;

                // check for InventorySlot component in hit or parent
                var slot = result.gameObject.GetComponentInParent<InventorySlot>();
                if (slot != null)
                    return slot.gameObject;
            }
        }

        // 3) If physics wasn't preferred earlier, try it now as a fallback
        if (!usePhysicsRaycast)
        {
            var physicsHit = PhysicsRaycastAtPointer(screenPos, cam);
            if (physicsHit != null)
                return physicsHit;
        }

        return null;
    }

    /// <summary>
    /// Runs a physics raycast/overlap at the pointer position and returns the first object that has
    /// a DropZone or InventorySlot in its parent chain. Skips the dragged object so dragging won't block.
    /// For 2D, we use OverlapPointAll at the world point; for 3D, we do RaycastAll and check hits.
    /// </summary>
    private GameObject PhysicsRaycastAtPointer(Vector2 screenPos, Camera cam)
    {
        if (cam == null)
        {
            cam = Camera.main;
            if (cam == null)
            {
                Debug.LogWarning("[InventorySlot] No camera found for physics raycast.");
                return null;
            }
        }

        if (use2DPhysics)
        {
            // Convert screen point to world point on camera's near plane (best effort).
            Vector3 worldPoint = cam.ScreenToWorldPoint(new Vector3(screenPos.x, screenPos.y, cam.nearClipPlane));
            Vector2 p2 = new Vector2(worldPoint.x, worldPoint.y);

            // OverlapPointAll finds colliders at that world point. We sort hits by depth (z distance to camera) to prioritize closer items.
            Collider2D[] hits = Physics2D.OverlapPointAll(p2, physicsLayerMask);
            if (hits == null || hits.Length == 0) return null;

            System.Array.Sort(hits, (a, b) =>
            {
                float da = Mathf.Abs(a.transform.position.z - cam.transform.position.z);
                float db = Mathf.Abs(b.transform.position.z - cam.transform.position.z);
                return da.CompareTo(db);
            });

            foreach (var h in hits)
            {
                if (h == null || h.gameObject == null) continue;

                // Skip the dragged object (or children of it)
                if (childToDrag != null && h.transform.IsChildOf(childToDrag)) continue;

                // Prefer InventorySlot on the collider object or its parents
                var slot = h.gameObject.GetComponentInParent<InventorySlot>();
                if (slot != null)
                    return slot.gameObject;

                var dz = h.gameObject.GetComponentInParent<DropZone>();
                if (dz != null)
                    return dz.gameObject;
            }
        }
        else
        {
            // 3D physics raycast from camera through screen point
            Ray ray = cam.ScreenPointToRay(screenPos);
            RaycastHit[] hits = Physics.RaycastAll(ray, Mathf.Infinity, physicsLayerMask);
            if (hits == null || hits.Length == 0) return null;

            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (var h in hits)
            {
                if (h.collider == null || h.collider.gameObject == null) continue;

                if (childToDrag != null && h.collider.transform.IsChildOf(childToDrag)) continue;

                var slot = h.collider.gameObject.GetComponentInParent<InventorySlot>();
                if (slot != null)
                    return slot.gameObject;

                var dz = h.collider.gameObject.GetComponentInParent<DropZone>();
                if (dz != null)
                    return dz.gameObject;
            }
        }

        return null;
    }
}
