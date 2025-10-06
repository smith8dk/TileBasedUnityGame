// RadialHitboxDispatcher.cs
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI; // required for Image

[RequireComponent(typeof(RectTransform))]
[RequireComponent(typeof(UnityEngine.UI.Image))]
public class RadialHitboxDispatcher : MonoBehaviour,
    IPointerClickHandler, IPointerEnterHandler, IPointerExitHandler, IPointerMoveHandler,
    IPointerDownHandler, IPointerUpHandler,
    IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
{
    public RMF_RadialMenu owner;

    // --- Hold-to-pickup state ---
    private bool isHolding = false;
    private float holdTimer = 0f;
    private float holdThreshold = 1f; // read from InventorySlot.holdTime when possible
    private Vector2 holdStartPos;
    private GameObject holdSlotGO;

    // dragging-from-slot (pickup) state
    private bool isPickupDragging = false;
    private Draggable activeDraggable = null;
    private CanvasGroup activeCanvasGroup = null;

    // store original WORLD transform values to preserve visual size/orientation
    private Vector3 pickupOriginalWorldScale = Vector3.one;
    private Quaternion pickupOriginalWorldRotation = Quaternion.identity;

    // pointer movement threshold to cancel hold (pixels)
    private const float HOLD_CANCEL_MOVE_DIST = 12f;

    // suppress the very next click after a successful drop (prevents drop->click)
    private bool suppressNextClick = false;
    private float lastSuppressTime = 0f;
    private const float SUPPRESS_TIMEOUT = 0.25f; // only suppress clicks that occur within this seconds of setting flag

    void Awake()
    {
        // defensive reset so first click isn't accidentally swallowed by stale state
        suppressNextClick = false;
        lastSuppressTime = 0f;
    }

    void OnEnable()
    {
        // ensure suppression doesn't persist across enable/disable
        suppressNextClick = false;
    }

    // --- Utility: run fresh EventSystem raycast at pointer and return all hits ---
    private static List<RaycastResult> RaycastAllAt(Vector2 screenPos)
    {
        var results = new List<RaycastResult>();
        if (EventSystem.current == null) return results;
        var pd = new PointerEventData(EventSystem.current) { position = screenPos };
        EventSystem.current.RaycastAll(pd, results);
        return results;
    }

    // Helper used while pickup is active: should we ignore this GameObject because it's a draggable
    // that is not the activeDraggable? Returns true if the dispatcher should skip this object.
    private bool ShouldSkipDuringPickup(GameObject go)
    {
        if (!isPickupDragging || activeDraggable == null || go == null) return false;

        // If the candidate object is the active draggable (or a child of it) -> do NOT skip.
        if (go == activeDraggable.gameObject || go.transform.IsChildOf(activeDraggable.transform)) return false;

        // If candidate (or its parents) contains a Draggable -> skip it (we don't want other draggables reacting).
        var otherDr = go.GetComponentInParent<Draggable>();
        if (otherDr != null) return true;

        return false;
    }

    // Find first underlying GameObject (excluding dispatcher and its children) that has an event handler of T.
    // Returns the handler GameObject and also the RaycastResult hit used.
    private bool TryFindUnderlyingHandler<T>(PointerEventData sourceEvent, out GameObject handler, out RaycastResult hit) where T : IEventSystemHandler
    {
        handler = null;
        hit = new RaycastResult();

        if (EventSystem.current == null) return false;

        var results = RaycastAllAt(sourceEvent.position);
        foreach (var rr in results)
        {
            if (rr.gameObject == null) continue;
            // skip dispatcher visuals and children
            if (rr.gameObject == gameObject) continue;
            if (rr.gameObject.transform.IsChildOf(transform)) continue;
            // skip the dragged object (it can block the raycast while dragged)
            if (sourceEvent.pointerDrag != null && rr.gameObject.transform.IsChildOf(sourceEvent.pointerDrag.transform)) continue;

            // NEW: If a pickup drag is in progress, avoid returning other draggables (so they won't be picked up too)
            if (ShouldSkipDuringPickup(rr.gameObject)) continue;

            // --- NEW: For click handlers, skip targets that are on cooldown ---
            // This prevents clicks from reaching EventClick / other click handlers when the draggable is cooling down,
            // while still allowing other pointer events (down/drag) to function normally.
            bool isLookingForClickHandler = typeof(T) == typeof(IPointerClickHandler);
            if (isLookingForClickHandler)
            {
                var cd = rr.gameObject.GetComponentInParent<DraggableCooldown>();
                if (cd != null)
                {
                    try
                    {
                        if (cd.IsOnCooldown)
                        {
                            // skip this candidate — it's cooling down and should not receive click events
                            Debug.LogFormat("[RadialHitboxDispatcher] Skipping click handler on '{0}' because DraggableCooldown.IsOnCooldown == true", rr.gameObject.name);
                            continue;
                        }
                    }
                    catch
                    {
                        // if anything goes wrong inspecting cooldown, don't block click — fail open
                    }
                }
            }

            var found = ExecuteEvents.GetEventHandler<T>(rr.gameObject);
            if (found != null)
            {
                handler = found;
                hit = rr;
                return true;
            }
        }

        return false;
    }

    // Find the first underlying InventorySlot or DropZone (skipping dispatcher and the dragged object).
    // Returns the slot gameObject and the RaycastResult.
    private bool TryFindUnderlyingSlotByRaycast(PointerEventData sourceEvent, out GameObject slotGO, out RaycastResult hit)
    {
        slotGO = null;
        hit = new RaycastResult();
        if (EventSystem.current == null) return false;

        var results = RaycastAllAt(sourceEvent.position);
        foreach (var rr in results)
        {
            if (rr.gameObject == null) continue;
            if (rr.gameObject == gameObject) continue;
            if (rr.gameObject.transform.IsChildOf(transform)) continue;
            if (sourceEvent.pointerDrag != null && rr.gameObject.transform.IsChildOf(sourceEvent.pointerDrag.transform)) continue;

            // NEW: Skip other exposed draggables while dispatcher is in pickup mode
            if (ShouldSkipDuringPickup(rr.gameObject)) continue;

            // Prefer InventorySlot component in parent chain
            var slot = rr.gameObject.GetComponentInParent<InventorySlot>();
            if (slot != null)
            {
                slotGO = slot.gameObject;
                hit = rr;
                return true;
            }

            // Otherwise prefer DropZone in parent chain
            var dz = rr.gameObject.GetComponentInParent<DropZone>();
            if (dz != null)
            {
                slotGO = dz.gameObject;
                hit = rr;
                return true;
            }
        }

        return false;
    }

    // Generic forwarder: updates eventData.pointerCurrentRaycast to the underlying hit (if found) and executes event on handler
    private void ForwardEvent<T>(PointerEventData eventData, ExecuteEvents.EventFunction<T> fn) where T : IEventSystemHandler
    {
        if (TryFindUnderlyingHandler<T>(eventData, out GameObject handler, out RaycastResult hit))
        {
            // update pointerCurrentRaycast so recipients that check it (e.g. Draggable) see accurate info
            eventData.pointerCurrentRaycast = hit;
            ExecuteEvents.Execute(handler, eventData, fn);
        }
    }

    // Notify RMF owner about pointer location so selection logic keeps working
    private void NotifyOwner(Vector2 screenPos)
    {
        owner?.ProcessPointer(screenPos);
    }

    void Update()
    {
        // manage hold timer logic
        if (isHolding && !isPickupDragging)
        {
            holdTimer += Time.deltaTime;

            // cancel if pointer moved too far
            if (Vector2.Distance(holdStartPos, Input.mousePosition) > HOLD_CANCEL_MOVE_DIST)
            {
                CancelHold();
            }
            else
            {
                if (holdTimer >= holdThreshold)
                {
                    // begin hold activation (handles Holding special-case)
                    HandleHoldActivation(holdSlotGO);
                    // reset hold state (HandleHoldActivation manages state)
                    isHolding = false;
                }
            }
        }

        // If we started a pickup drag, drive the dragged object to the pointer and call its OnDrag for consistency
        if (isPickupDragging && activeDraggable != null)
        {
            // keep object following cursor
            activeDraggable.transform.position = Input.mousePosition;

            // call OnDrag on the Draggable for any internal logic
            if (EventSystem.current != null)
            {
                var pd = new PointerEventData(EventSystem.current) { position = Input.mousePosition, pointerDrag = activeDraggable.gameObject };
                try
                {
                    ExecuteEvents.Execute(activeDraggable.gameObject, pd, ExecuteEvents.dragHandler);
                }
                catch { /* swallow exceptions from user code */ }
            }
        }
    }

    // --- Basic pointer events (notify owner + forward) ---
    public void OnPointerMove(PointerEventData eventData)
    {
        NotifyOwner(eventData.position);
        ForwardEvent<IPointerMoveHandler>(eventData, ExecuteEvents.pointerMoveHandler);
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        NotifyOwner(eventData.position);
        ForwardEvent<IPointerEnterHandler>(eventData, ExecuteEvents.pointerEnterHandler);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        owner?.ClearPointer();
        ForwardEvent<IPointerExitHandler>(eventData, ExecuteEvents.pointerExitHandler);
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // If we've just handled a drop, suppress the immediately following click (prevents drop->click)
        if (suppressNextClick)
        {
            // only suppress if recent
            if (Time.time - lastSuppressTime <= SUPPRESS_TIMEOUT)
            {
                suppressNextClick = false;
                lastSuppressTime = 0f;
                Debug.Log("[RadialHitboxDispatcher] Suppressed a click due to recent drag/drop.");
                return;
            }
            else
            {
                // stale suppression, just clear it
                suppressNextClick = false;
                lastSuppressTime = 0f;
            }
        }

        NotifyOwner(eventData.position);

        // forward click to underlying handler (if any)
        ForwardEvent<IPointerClickHandler>(eventData, ExecuteEvents.pointerClickHandler);

        // preserve RMF behavior: also let owner handle click
        owner?.ProcessClick(eventData.position);
    }

    // When pointer down we potentially start a hold (if pointer down on a slot that has a child item)
    public void OnPointerDown(PointerEventData eventData)
    {
        // Notify owner early so selection/hover matches clicks
        NotifyOwner(eventData.position);

        // --- IMPORTANT: dispatcher takes control of pointerDown when owner maps to a slot
        // Ask owner which slot is selected at this screen point (preferred — matches clicks)
        GameObject slotGO = null;
        RectTransform slotRT = null;
        if (owner != null)
        {
            slotRT = owner.GetSlotAtScreenPoint(eventData.position);
            if (slotRT != null) slotGO = slotRT.gameObject;
        }

        // fallback: raycast-based if owner didn't return a slot
        if (slotGO == null)
        {
            if (TryFindUnderlyingSlotByRaycast(eventData, out GameObject rrSlot, out RaycastResult rrHit))
                slotGO = rrSlot;
        }

        // If we have no owner slot for this pointer, forward pointerDown as before.
        if (slotGO == null)
        {
            // forward pointer down to the underlying handler (preserve old behavior)
            ForwardEvent<IPointerDownHandler>(eventData, ExecuteEvents.pointerDownHandler);
            return;
        }

        // If found a slot and slot has a child, prepare to hold (dispatcher handles the hold/pickup).
        // Note: we do NOT forward the pointerDown to underlying handlers here to avoid duplicate hold pickups.
        if (slotGO != null && slotGO.transform.childCount > 0)
        {
            holdSlotGO = slotGO;
            holdTimer = 0f;
            isHolding = true;
            holdStartPos = Input.mousePosition;

            // attempt to read InventorySlot.holdTime via reflection; default to 1s if not found
            float ht = 1f;
            var slotComp = slotGO.GetComponent<InventorySlot>();
            if (slotComp != null)
            {
                var fi = typeof(InventorySlot).GetField("holdTime", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                if (fi != null)
                {
                    try
                    {
                        object val = fi.GetValue(slotComp);
                        if (val is float f) ht = f;
                    }
                    catch { /* ignore reflection errors */ }
                }
            }
            holdThreshold = ht;

            // IMPORTANT: do NOT call ForwardEvent<IPointerDownHandler> here. If the underlying InventorySlot
            // has its own hold logic it would also start a pickup. The dispatcher takes responsibility for pickup.
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        NotifyOwner(eventData.position);
        ForwardEvent<IPointerUpHandler>(eventData, ExecuteEvents.pointerUpHandler);

        // cancel hold if pointer released early
        if (isHolding)
            CancelHold();

        // if we initiated pickup drag ourselves, finalize it here
        if (isPickupDragging)
        {
            // Build a small PointerEventData for finalization. FinalizePickupDrag will ensure pointerDrag is set.
            var pd = (EventSystem.current != null) ? new PointerEventData(EventSystem.current) { position = eventData.position } : null;
            FinalizePickupDrag(pd ?? eventData);
        }
    }

    private void CancelHold()
    {
        isHolding = false;
        holdTimer = 0f;
        holdSlotGO = null;
    }

    // --- Drag / Drop handling (normal forwarded drag flow) ---
    public void OnBeginDrag(PointerEventData eventData)
    {
        NotifyOwner(eventData.position);

        // SPECIAL: if the object being dragged is a "Holding" item currently inside the radial menu,
        // immediately move it to salvage parent, call owner.RemoveHoldingSlot(), and cancel the drag.
        if (eventData.pointerDrag != null && owner != null)
        {
            var draggedGO = eventData.pointerDrag;
            if (string.Equals(draggedGO.name, "Holding", StringComparison.OrdinalIgnoreCase))
            {
                // Determine if this draggedGO is currently a child of the menu's slotParentContainer (i.e. in the radial menu)
                var container = owner.slotParentContainer != null ? owner.slotParentContainer : owner.rt as Transform;
                if (container != null && draggedGO.transform.IsChildOf(container))
                {
                    MoveToSalvageAndRemoveHolding(draggedGO);
                    // suppress click and cancel forwarding
                    suppressNextClick = true;
                    lastSuppressTime = Time.time;
                    return; // skip forwarding BeginDrag
                }
            }
        }

        // otherwise forward normally
        ForwardEvent<IBeginDragHandler>(eventData, ExecuteEvents.beginDragHandler);
    }

    public void OnDrag(PointerEventData eventData)
    {
        NotifyOwner(eventData.position);
        ForwardEvent<IDragHandler>(eventData, ExecuteEvents.dragHandler);
    }

    // Centralized finalization used both when EventSystem calls OnEndDrag and when dispatcher started the pickup.
    public void OnEndDrag(PointerEventData eventData)
    {
        NotifyOwner(eventData.position);

        // If we started pickup drag, ensure the draggable receives EndDrag first
        if (isPickupDragging && activeDraggable != null)
        {
            try
            {
                var pdForDraggable = (EventSystem.current != null) ? new PointerEventData(EventSystem.current) { position = eventData.position, pointerDrag = activeDraggable.gameObject } : eventData;
                ExecuteEvents.Execute(activeDraggable.gameObject, pdForDraggable, ExecuteEvents.endDragHandler);
            }
            catch { }
        }

        // forward EndDrag to any underlying handler (existing behavior)
        ForwardEvent<IEndDragHandler>(eventData, ExecuteEvents.endDragHandler);

        // Now handle drop finalization (prefer owner's sector-based slot, fallback to raycast)
        GameObject slotGO = null;
        RaycastResult chosenHit = new RaycastResult();

        if (owner != null)
        {
            var slotRT = owner.GetSlotAtScreenPoint(eventData.position);
            if (slotRT != null)
            {
                slotGO = slotRT.gameObject;
                chosenHit = new RaycastResult { gameObject = slotGO };
            }
        }

        if (slotGO == null)
        {
            if (TryFindUnderlyingSlotByRaycast(eventData, out GameObject rrSlot, out RaycastResult rrHit))
            {
                slotGO = rrSlot;
                chosenHit = rrHit;
            }
        }

        if (slotGO != null)
        {
            // update pointerCurrentRaycast for Draggable.FinalizeDropCoroutine and blacklist checks
            eventData.pointerCurrentRaycast = chosenHit;

            // If we have an activeDraggable (we started pickup), set its parentAfterDrag and reparent it now
            if (isPickupDragging && activeDraggable != null)
            {
                try
                {
                    // Check capacity before placing
                    if (!IsSlotAvailable(slotGO))
                    {
                        Debug.LogFormat("[RadialHitboxDispatcher] (pickup) target slot '{0}' is full — not placing draggable '{1}'", slotGO.name, activeDraggable.name);
                        // Don't reparent — let Draggable finalize and restore to originalParent by its own logic
                    }
                    else
                    {
                        // set parentAfterDrag for the Draggable logic
                        activeDraggable.parentAfterDrag = slotGO.transform;

                        // compute local scale needed to preserve original world scale after reparent
                        Vector3 parentLossy = slotGO.transform.lossyScale;
                        Vector3 desiredLocal = new Vector3(
                            pickupOriginalWorldScale.x / Mathf.Max(1e-6f, parentLossy.x),
                            pickupOriginalWorldScale.y / Mathf.Max(1e-6f, parentLossy.y),
                            pickupOriginalWorldScale.z / Mathf.Max(1e-6f, parentLossy.z)
                        );

                        // reparent (keep false to match prior behavior)
                        activeDraggable.transform.SetParent(slotGO.transform, false);

                        // restore world rotation and scale (by setting local scale computed above)
                        activeDraggable.transform.rotation = pickupOriginalWorldRotation;
                        activeDraggable.transform.localScale = desiredLocal;

                        Debug.LogFormat("[RadialHitboxDispatcher] Reparented dragged '{0}' to slot '{1}' (pickup flow)", activeDraggable.name, slotGO.name);

                        // mark suppression (drop happened)
                        suppressNextClick = true;
                        lastSuppressTime = Time.time;
                    }
                }
                catch (Exception ex) { Debug.LogException(ex); }
            }

            // forward drop event to slot GameObject so InventorySlot.OnDrop runs
            ExecuteEvents.Execute(slotGO, eventData, ExecuteEvents.dropHandler);

            // If the object that was dropped is a Holding-like item, inform owner to increase visual slots
            if (eventData.pointerDrag != null)
            {
                var heldObj = eventData.pointerDrag;
                if (string.Equals(heldObj.name, "Holding", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        owner?.AddHoldingSlot();
                    }
                    catch { }
                }

                // If it's an Enhance or Heart, notify the owner menu so it can update outlines/HP
                if (owner != null)
                {
                    try
                    {
                        if (heldObj.name.IndexOf("Enhance", StringComparison.OrdinalIgnoreCase) >= 0)
                            owner.OnEnhancePlaced(heldObj);
                        if (heldObj.name.IndexOf("Heart", StringComparison.OrdinalIgnoreCase) >= 0)
                            owner.OnHeartPlaced(heldObj);
                    }
                    catch { }
                }
            }

            // invoke static events directly so external subscribers (e.g. MenuHandler) receive notifications
            try { InvokeStaticOnItemDraggedOrDropped(typeof(Draggable)); } catch { }
            try { InvokeStaticOnItemDraggedOrDropped(typeof(InventorySlot)); } catch { }

            // suppress the immediate next click if this was a drag-drop (or pickup drag)
            if (eventData.pointerDrag != null || isPickupDragging)
            {
                suppressNextClick = true;
                lastSuppressTime = Time.time;
            }
        }
        else
        {
            // no slot found; forward Drop to any underlying handler
            ForwardEvent<IDropHandler>(eventData, ExecuteEvents.dropHandler);
        }

        // cleanup pickup state
        if (isPickupDragging)
            CleanupPickupState();
    }

    public void OnDrop(PointerEventData eventData)
    {
        NotifyOwner(eventData.position);

        // Mirror OnEndDrag behavior: prefer owner-sector, fallback to raycast
        GameObject slotGO = null;
        RaycastResult chosenHit = new RaycastResult();

        if (owner != null)
        {
            var slotRT = owner.GetSlotAtScreenPoint(eventData.position);
            if (slotRT != null)
            {
                slotGO = slotRT.gameObject;
                chosenHit = new RaycastResult { gameObject = slotGO };
            }
        }

        if (slotGO == null)
        {
            if (TryFindUnderlyingSlotByRaycast(eventData, out GameObject rrSlot, out RaycastResult rrHit))
            {
                slotGO = rrSlot;
                chosenHit = rrHit;
            }
        }

        if (slotGO != null)
        {
            eventData.pointerCurrentRaycast = chosenHit;

            // If pointerDrag is present, set its parentAfterDrag etc.
            GameObject dragged = eventData.pointerDrag;
            if (dragged != null)
            {
                var draggable = dragged.GetComponent<Draggable>() ?? dragged.GetComponentInChildren<Draggable>();
                if (draggable != null)
                {
                    try
                    {
                        // Check slot capacity before placing
                        if (!IsSlotAvailable(slotGO))
                        {
                            Debug.LogFormat("[RadialHitboxDispatcher] (OnDrop) target slot '{0}' is full — refusing to place '{1}'", slotGO.name, draggable.name);
                            // fallback: forward drop to underlying handlers (so other UI can handle) and do not reparent
                            ForwardEvent<IDropHandler>(eventData, ExecuteEvents.dropHandler);
                            return;
                        }

                        // Save current world rotation & world scale so we can restore them after reparent
                        Quaternion savedWorldRot = draggable.transform.rotation;
                        Vector3 currentWorldScale = draggable.transform.lossyScale;

                        // Compute local scale required under the new parent so world scale equals currentWorldScale
                        Vector3 parentLossy = slotGO.transform.lossyScale;
                        Vector3 desiredLocal = new Vector3(
                            currentWorldScale.x / Mathf.Max(1e-6f, parentLossy.x),
                            currentWorldScale.y / Mathf.Max(1e-6f, parentLossy.y),
                            currentWorldScale.z / Mathf.Max(1e-6f, parentLossy.z)
                        );

                        draggable.parentAfterDrag = slotGO.transform;
                        draggable.transform.SetParent(slotGO.transform, false);

                        // Restore world rotation and apply computed local scale to preserve world size
                        draggable.transform.rotation = savedWorldRot;
                        draggable.transform.localScale = desiredLocal;

                        Debug.LogFormat("[RadialHitboxDispatcher] (OnDrop) Reparented dragged '{0}' to slot '{1}'", draggable.name, slotGO.name);

                        // mark suppression (drop happened)
                        suppressNextClick = true;
                        lastSuppressTime = Time.time;

                        // If the dropped object is "Holding", inform owner to increase slots
                        if (string.Equals(dragged.name, "Holding", StringComparison.OrdinalIgnoreCase))
                        {
                            try { owner?.AddHoldingSlot(); } catch { }
                        }
                    }
                    catch (Exception ex) { Debug.LogException(ex); }
                }
            }

            ExecuteEvents.Execute(slotGO, eventData, ExecuteEvents.dropHandler);

            // If it's an Enhance or Heart, notify the owner
            if (eventData.pointerDrag != null && owner != null)
            {
                var heldObj = eventData.pointerDrag;
                try
                {
                    if (heldObj.name.IndexOf("Enhance", StringComparison.OrdinalIgnoreCase) >= 0)
                        owner.OnEnhancePlaced(heldObj);
                    if (heldObj.name.IndexOf("Heart", StringComparison.OrdinalIgnoreCase) >= 0)
                        owner.OnHeartPlaced(heldObj);
                }
                catch { }
            }

            // invoke static events directly so external subscribers (e.g. MenuHandler) receive notifications
            try { InvokeStaticOnItemDraggedOrDropped(typeof(Draggable)); } catch { }
            try { InvokeStaticOnItemDraggedOrDropped(typeof(InventorySlot)); } catch { }

            if (eventData.pointerDrag != null)
            {
                suppressNextClick = true;
                lastSuppressTime = Time.time;
            }
        }
        else
        {
            ForwardEvent<IDropHandler>(eventData, ExecuteEvents.dropHandler);
        }
    }

    // Start a pickup drag programmatically from a slot's first child (mirrors InventorySlot.StartDraggingFirstChild())
    private void StartPickupDragFromSlot(GameObject slotGO)
    {
        if (slotGO == null) return;
        if (slotGO.transform.childCount == 0) return;

        // pick the first child as the draggable candidate (same as InventorySlot)
        var child = slotGO.transform.GetChild(0);
        if (child == null) return;

        var draggable = child.GetComponent<Draggable>() ?? child.GetComponentInChildren<Draggable>();
        if (draggable == null) return;

        // store original WORLD scale & rotation to preserve visuals after BeginDrag
        pickupOriginalWorldScale = draggable.transform.lossyScale;
        pickupOriginalWorldRotation = draggable.transform.rotation;

        // add/get CanvasGroup and disable blocking so pointer can hit slots under the dragged object
        var cg = draggable.GetComponent<CanvasGroup>();
        if (cg == null) cg = draggable.gameObject.AddComponent<CanvasGroup>();
        cg.blocksRaycasts = false;

        // create pointer event data for BeginDrag and include pointerDrag so handlers see the correct source
        var pd = (EventSystem.current != null)
            ? new PointerEventData(EventSystem.current) { position = Input.mousePosition, pointerDrag = draggable.gameObject }
            : null;

        // Fire BeginDrag so other handlers can react (this will usually reparent to root etc.)
        try
        {
            ExecuteEvents.Execute(draggable.gameObject, pd ?? new PointerEventData(EventSystem.current), ExecuteEvents.beginDragHandler);
        }
        catch { /* swallow user exceptions */ }

        // After BeginDrag the draggable may have been reparented: compute local scale that preserves world scale
        try
        {
            var parent = draggable.transform.parent;
            Vector3 parentLossy = (parent != null) ? parent.lossyScale : Vector3.one;
            Vector3 desiredLocal = new Vector3(
                pickupOriginalWorldScale.x / Mathf.Max(1e-6f, parentLossy.x),
                pickupOriginalWorldScale.y / Mathf.Max(1e-6f, parentLossy.y),
                pickupOriginalWorldScale.z / Mathf.Max(1e-6f, parentLossy.z)
            );

            draggable.transform.localScale = desiredLocal;
            draggable.transform.rotation = pickupOriginalWorldRotation;
        }
        catch { /* swallow transform errors */ }

        // set pickup state so Update will drive dragging and OnEndDrag will finalize
        isPickupDragging = true;
        activeDraggable = draggable;
        activeCanvasGroup = cg;

        // optional debug
        Debug.LogFormat("[RadialHitboxDispatcher] Pickup drag started for '{0}'", activeDraggable.name);
    }

    // Called to finalize pickup drag (when dispatcher starts it and pointer released)
    private void FinalizePickupDrag(PointerEventData eventData)
    {
        if (!isPickupDragging) return;

        // Ensure the draggable receives an EndDrag event and build an eventData that includes pointerDrag
        if (activeDraggable != null)
        {
            try
            {
                var pdForDraggable = (EventSystem.current != null)
                    ? new PointerEventData(EventSystem.current) { position = (eventData != null) ? eventData.position : Input.mousePosition, pointerDrag = activeDraggable.gameObject }
                    : eventData;

                ExecuteEvents.Execute(activeDraggable.gameObject, pdForDraggable, ExecuteEvents.endDragHandler);
            }
            catch { }
        }

        // Build an eventData that definitely has pointerDrag set so OnEndDrag and OnDrop logic sees the dragged object.
        PointerEventData eventDataForOnEnd;
        if (eventData != null && eventData.pointerDrag != null)
        {
            eventDataForOnEnd = eventData;
        }
        else
        {
            eventDataForOnEnd = (EventSystem.current != null)
                ? new PointerEventData(EventSystem.current) { position = (eventData != null) ? eventData.position : Input.mousePosition, pointerDrag = (activeDraggable != null) ? activeDraggable.gameObject : null }
                : eventData;
        }

        // Now perform the same drop finalization used in OnEndDrag by calling OnEndDrag with ensured pointerDrag
        OnEndDrag(eventDataForOnEnd);
    }

    private void CleanupPickupState()
    {
        if (activeCanvasGroup != null)
        {
            try
            {
                // re-enable blocking — Draggable.FinalizeDropCoroutine will run and may further adjust
                activeCanvasGroup.blocksRaycasts = true;
            }
            catch { }
        }

        isPickupDragging = false;
        activeDraggable = null;
        activeCanvasGroup = null;
    }

    // ---------- Helpers to invoke static events if they exist ----------
    // Robustly tries multiple backing-field naming patterns and falls back to scanning static delegate fields.
    private void InvokeStaticOnItemDraggedOrDropped(Type t)
    {
        if (t == null) return;

        try
        {
            // 1) Try to find a backing field with the event name (public or non-public)
            FieldInfo fi = t.GetField("OnItemDraggedOrDropped", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (fi != null)
            {
                var d = fi.GetValue(null) as Delegate;
                if (d != null)
                {
                    foreach (var del in d.GetInvocationList())
                    {
                        try { del.DynamicInvoke(); } catch { /* swallow subscriber exceptions */ }
                    }
                    return;
                }
            }

            // 2) Try common compiler-generated backing field pattern
            fi = t.GetField("<OnItemDraggedOrDropped>k__BackingField", BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            if (fi != null)
            {
                var d = fi.GetValue(null) as Delegate;
                if (d != null)
                {
                    foreach (var del in d.GetInvocationList())
                    {
                        try { del.DynamicInvoke(); } catch { }
                    }
                    return;
                }
            }

            // 3) If event exists, try its raise method (custom events)
            var ei = t.GetEvent("OnItemDraggedOrDropped", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            if (ei != null)
            {
                var rm = ei.GetRaiseMethod(true);
                if (rm != null)
                {
                    rm.Invoke(null, null);
                    return;
                }
            }

            // 4) Fallback: scan all static fields for Delegate types and try to find one whose name mentions the event.
            var fields = t.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            foreach (var f in fields)
            {
                if (!typeof(Delegate).IsAssignableFrom(f.FieldType)) continue;
                if (!f.Name.Contains("OnItemDraggedOrDropped", StringComparison.OrdinalIgnoreCase) && !f.Name.Contains("DraggedOrDropped", StringComparison.OrdinalIgnoreCase)) continue;
                var d = f.GetValue(null) as Delegate;
                if (d != null)
                {
                    foreach (var del in d.GetInvocationList())
                    {
                        try { del.DynamicInvoke(); } catch { }
                    }
                    return;
                }
            }

            // 5) Final brute-force fallback: invoke any static Delegate fields found (last resort; kept conservative)
            foreach (var f in fields)
            {
                if (!typeof(Delegate).IsAssignableFrom(f.FieldType)) continue;
                var d = f.GetValue(null) as Delegate;
                if (d != null)
                {
                    foreach (var del in d.GetInvocationList())
                    {
                        try { del.DynamicInvoke(); } catch { }
                    }
                    // don't return — continue trying others for completeness
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarningFormat("[RadialHitboxDispatcher] InvokeStaticOnItemDraggedOrDropped reflection failed for {0}: {1}", t.Name, ex.Message);
        }
    }

    /// <summary>
    /// Check whether the given slot GameObject has capacity for another Draggable.
    /// Attempts to read InventorySlot.InventorySize via reflection; if missing, assumes 1.
    /// Returns true if slotGO.childCount < capacity.
    /// </summary>
    private bool IsSlotAvailable(GameObject slotGO)
    {
        if (slotGO == null) return false;

        int capacity = 1; // default
        var slotComp = slotGO.GetComponent<InventorySlot>();
        if (slotComp != null)
        {
            // try common field names
            var fi = slotComp.GetType().GetField("InventorySize", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (fi == null)
                fi = slotComp.GetType().GetField("inventorySize", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            if (fi != null)
            {
                try
                {
                    object val = fi.GetValue(slotComp);
                    if (val is int vi) capacity = vi;
                }
                catch { }
            }
            else
            {
                // fallback: try a property
                var pi = slotComp.GetType().GetProperty("InventorySize", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (pi != null)
                {
                    try
                    {
                        object val = pi.GetValue(slotComp);
                        if (val is int vi2) capacity = vi2;
                    }
                    catch { }
                }
            }
        }

        // finally check children count (direct children count)
        int childCount = slotGO.transform.childCount;
        return childCount < Mathf.Max(1, capacity);
    }

    /// <summary>
    /// (Optional) Allows toggling the dispatcher's graphic raycast target if you prefer to temporarily
    /// disable it while dragging instead of programmatically forwarding. By default the dispatcher forwards.
    /// </summary>
    public void SetRaycastTarget(bool enabled)
    {
        var img = GetComponent<UnityEngine.UI.Image>();
        if (img != null) img.raycastTarget = enabled;
    }

    // ------------------- Holding helpers -------------------

    /// <summary>
    /// Called when hold threshold reached on a slot. If the slot's first child is a "Holding" draggable,
    /// move it to salvage parent and remove a slot. Otherwise start normal pickup from slot.
    /// Robustness: re-evaluates the slot under the current pointer position using the owner's sector math
    /// first (so SHIFT-sector mapping is respected), then falls back to raycast-based slot detection,
    /// then to the original stored slot.
    /// </summary>
    private void HandleHoldActivation(GameObject originalSlot)
    {
        GameObject slotToUse = null;

        // 1) Try asking owner with current pointer position so sector-mapping is respected
        if (owner != null)
        {
            try
            {
                var rt = owner.GetSlotAtScreenPoint(Input.mousePosition, false);
                if (rt != null) slotToUse = rt.gameObject;
            }
            catch { /* ignore */ }
        }

        // 2) Fallback to raycast-based lookup at current pointer pos
        if (slotToUse == null && EventSystem.current != null)
        {
            try
            {
                var pd = new PointerEventData(EventSystem.current) { position = Input.mousePosition };
                if (TryFindUnderlyingSlotByRaycast(pd, out GameObject rrSlot, out RaycastResult rrHit))
                    slotToUse = rrSlot;
            }
            catch { /* ignore */ }
        }

        // 3) Final fallback to the originally stored slot from OnPointerDown
        if (slotToUse == null)
            slotToUse = originalSlot;

        if (slotToUse == null)
        {
            // nothing to do
            isHolding = false;
            holdSlotGO = null;
            holdTimer = 0f;
            return;
        }

        if (slotToUse.transform.childCount == 0)
        {
            // nothing to pick up
            isHolding = false;
            holdSlotGO = null;
            holdTimer = 0f;
            return;
        }

        Transform child = slotToUse.transform.GetChild(0);
        if (child == null)
        {
            isHolding = false;
            holdSlotGO = null;
            holdTimer = 0f;
            return;
        }

        var dr = child.GetComponent<Draggable>() ?? child.GetComponentInChildren<Draggable>();
        GameObject childGO = (dr != null) ? dr.gameObject : child.gameObject;

        bool isHoldingItem = string.Equals(childGO.name, "Holding", StringComparison.OrdinalIgnoreCase);
        if (isHoldingItem && owner != null)
        {
            // Move the "Holding" object to salvage parent and remove a slot.
            MoveToSalvageImmediate(childGO, dr);
            try { owner.RemoveHoldingSlot(); } catch { }
            // Also, if removed object is Heart or Enhance, notify owner
            try
            {
                if (childGO.name.IndexOf("Heart", StringComparison.OrdinalIgnoreCase) >= 0) owner.OnHeartRemoved(childGO);
                if (childGO.name.IndexOf("Enhance", StringComparison.OrdinalIgnoreCase) >= 0) owner.OnEnhanceRemoved(childGO);
            }
            catch { }
            // reset hold state
            isHolding = false;
            holdSlotGO = null;
            holdTimer = 0f;
            return;
        }

        // otherwise normal pickup
        // BEFORE starting pickup, if this child is a Heart or Enhance and it's being removed via pickup,
        // notify owner so outlines/health are updated instantly.
        try
        {
            if (childGO.name.IndexOf("Heart", StringComparison.OrdinalIgnoreCase) >= 0)
                owner?.OnHeartRemoved(childGO);
            if (childGO.name.IndexOf("Enhance", StringComparison.OrdinalIgnoreCase) >= 0)
                owner?.OnEnhanceRemoved(childGO);
        }
        catch { }

        StartPickupDragFromSlot(slotToUse);
    }

    /// <summary>
    /// Moves the given GameObject into the owner's salvage parent immediately (preserving world transform)
    /// and re-enables its Image/SpriteRenderer if present.
    /// Also notifies owner if the object was a Heart or Enhance so menu state stays correct.
    /// </summary>
    private void MoveToSalvageImmediate(GameObject go, Draggable dr)
    {
        if (go == null || owner == null) return;

        // choose salvage parent from owner
        Transform targetParent = owner.salvageParent != null ? owner.salvageParent : (owner.slotParentContainer != null ? owner.slotParentContainer : owner.rt);
        if (targetParent == null) targetParent = owner.rt;

        Vector3 worldPos = go.transform.position;
        Quaternion worldRot = go.transform.rotation;
        Vector3 worldScale = go.transform.lossyScale;

        try
        {
            go.transform.SetParent(targetParent, true);
            go.transform.position = worldPos;
            go.transform.rotation = worldRot;

            Vector3 parentLossy = go.transform.parent ? go.transform.parent.lossyScale : Vector3.one;
            Vector3 desiredLocal = new Vector3(
                worldScale.x / Mathf.Max(1e-6f, parentLossy.x),
                worldScale.y / Mathf.Max(1e-6f, parentLossy.y),
                worldScale.z / Mathf.Max(1e-6f, parentLossy.z)
            );
            go.transform.localScale = desiredLocal;

            // re-enable visuals if disabled
            var img = go.GetComponent<Image>();
            if (img != null) img.enabled = true;
            var sr = go.GetComponent<SpriteRenderer>();
            if (sr != null) sr.enabled = true;

            // update draggable parentAfterDrag
            if (dr != null)
            {
                try { dr.parentAfterDrag = targetParent; } catch { }
            }

            Debug.LogFormat("[RadialHitboxDispatcher] Moved Holding '{0}' to salvage parent '{1}'", go.name, targetParent.name);

            // notify owner if special types
            try
            {
                if (go.name.IndexOf("Heart", StringComparison.OrdinalIgnoreCase) >= 0)
                    owner.OnHeartRemoved(go);
                if (go.name.IndexOf("Enhance", StringComparison.OrdinalIgnoreCase) >= 0)
                    owner.OnEnhanceRemoved(go);
            }
            catch { }
        }
        catch (Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    /// <summary>
    /// When the user begins dragging a Holding that is currently inside the radial menu,
    /// move it immediately to salvage and remove a slot.
    /// </summary>
    private void MoveToSalvageAndRemoveHolding(GameObject holdingGO)
    {
        if (holdingGO == null || owner == null) return;

        // attempt to find Draggable on the object
        var dr = holdingGO.GetComponent<Draggable>() ?? holdingGO.GetComponentInChildren<Draggable>();
        MoveToSalvageImmediate(holdingGO, dr);

        try { owner.RemoveHoldingSlot(); } catch { }
    }
}
