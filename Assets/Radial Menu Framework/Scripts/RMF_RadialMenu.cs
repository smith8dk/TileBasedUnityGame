// RMF_RadialMenu.cs
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

[AddComponentMenu("Radial Menu Framework/RMF Core Script - Responsive (Debuggable Sector Selection)")]
public class RMF_RadialMenu : MonoBehaviour
{
    [HideInInspector] public RectTransform rt;

    [Header("Prefab / Slot Setup")]
    public GameObject buttonPrefab;
    public List<GameObject> items = new List<GameObject>();
    public int slotCount = 8;

    [Header("Hierarchy")]
    [Tooltip("Where slots will be placed in the hierarchy. If empty, a child SlotsContainer will be auto-created.")]
    public Transform slotParentContainer;

    [Header("Arc / Rotation")]
    [Range(0.1f, 360f)]
    public float totalAngle = 360f;
    public float globalOffset = 0f;
    [Tooltip("Extra rotation applied to each slot (use this to align artwork).")]
    public float rotationOffset = 0f;
    [Tooltip("If true, rotationOffset is applied to selection math as well as visuals. If false, rotationOffset only rotates visuals.")]
    public bool rotationOffsetAffectsSelection = true;

    [Header("Slice Spacing")]
    [Tooltip("Degrees of empty space removed from each slice.")]
    [Range(0f, 45f)]
    public float sliceGapDegrees = 2f;

    [Header("Donut / Inner hole")]
    [Range(0f, 0.95f)]
    public float innerHoleScale = 0f; // fraction of outer radius

    [Header("Radius / Fitting (responsive)")]
    public bool useRadiusPercent = true;
    [Range(0.0f, 1.0f)]
    public float radiusPercent = 0.45f;
    public float absoluteRadius = 120f;
    public bool useAbsoluteRadius = false;
    public float radiusPadding = 6f;

    [Header("Scale / Fit")]
    public bool fitToBounds = true;
    public float prefabScaleMultiplier = 1f;
    public float minScale = 0.05f;
    public float maxScale = 2f;

    [Header("Hitbox Dispatcher")]
    public GameObject squareButtonPrefab;
    public float hitboxOuterPadding = 8f;
    public Transform dispatcherParentContainer;

    [Header("Selection / Input")]
    public bool useGamepad = false;
    public float gamepadDeadzone = 0.35f;
    public RectTransform selectionFollowerContainer;
    public bool keepChildrenUpright = true;

    [Header("Highlighting")]
    public bool enableHighlight = true;
    public float highlightScale = 1.15f;

    [Header("Auto Rebuild")]
    public bool autoRebuildOnSizeChange = true;
    public float boundsChangeThreshold = 0.5f;

    [Header("Debugging")]
    public bool debugLogs = false;
    public bool debugDrawGizmos = true;

    [SerializeField] private RectTransform boundsContainerRef;

    // --- Salvage settings (preserve draggable items when slots are rebuilt) ---
    [Header("Salvage (preserve draggable items when slots are rebuilt)")]
    [Tooltip("If true, Draggable items found inside slots during rebuild will be moved or duplicated to the salvage parent or persistent slots.")]
    public bool enableSalvage = true;

    public enum SalvageMode { Move, Duplicate }
    [Tooltip("Move = reparent the existing draggable. Duplicate = instantiate a copy in the salvage parent or persistent slot.")]
    public SalvageMode salvageMode = SalvageMode.Move;

    [Tooltip("Target parent for salvaged draggable items if no persistent slot is available. If empty, the slotParentContainer or this menu RectTransform will be used.")]
    public Transform salvageParent;

    [Header("Optional listener to refresh UI after salvage")]
    [Tooltip("If assigned, this MenuHandler's updateSpells() will be called after salvaged items are restored. If null, the script will try to find one automatically.")]
    public MenuHandler menuHandler;

    // runtime lists
    internal List<RectTransform> instantiatedSlots = new List<RectTransform>(); // made internal so helper methods can safely use it if needed
    private List<Vector3> slotBaseScales = new List<Vector3>();
    private int currentSelected = -1;

    // slot data (angles in boundsRT local space)
    // These arrays correspond to the *visible/real* slots used for selection
    private List<float> slotCenterAngles = new List<float>(); // degrees 0..360
    private List<float> slotBoundaryNext = new List<float>();

    // dispatcher
    private RectTransform dispatcherRT;
    private RadialHitboxDispatcher dispatcherComp;

    // cached layout & bounds info (boundsRT local space)
    private float cachedFinalRadius = 0f;
    private float cachedInnerRadius = 0f;
    private RectTransform cachedBoundsRT;
    private int cachedCount = 0;
    private float cachedSliceAngle = 0f;

    // cached canvas
    private Canvas rootCanvas;

    // rebuild detection
    private Vector2 lastBoundsSize = Vector2.zero;
    private float lastCanvasScale = 1f;
    private int lastScreenW = 0;
    private int lastScreenH = 0;

    // temporary salvage storage while rebuilding
    private List<Draggable> salvagedDraggables = new List<Draggable>();
    private Transform tempSalvageParent = null;

    // Extra slots added by Holding items (each Holding present in the menu should add exactly 1)
    private int extraSlotsFromHoldings = 0;

    public Action<int, RectTransform> onSelectionChanged;

    void Awake()
    {
        rt = GetComponent<RectTransform>();
        if (rt == null) Debug.LogError("Radial Menu: missing RectTransform on " + name);

        rootCanvas = GetComponentInParent<Canvas>();
        lastCanvasScale = (rootCanvas != null) ? rootCanvas.scaleFactor : 1f;

        // Auto-create slotParentContainer if none assigned
        if (slotParentContainer == null)
        {
            GameObject go = new GameObject(rt.name + "_Slots", typeof(RectTransform));
            go.transform.SetParent(rt, false);
            RectTransform r = go.GetComponent<RectTransform>();
            r.anchorMin = new Vector2(0.5f, 0.5f);
            r.anchorMax = new Vector2(0.5f, 0.5f);
            r.pivot = new Vector2(0.5f, 0.5f);
            r.anchoredPosition = Vector2.zero;
            r.sizeDelta = rt != null ? rt.rect.size : new Vector2(200, 200);
            slotParentContainer = r;
        }
        DumpSlotAngles();
    }

    void Start()
    {
        BuildMenu();
        CacheCurrentLayoutState();
    }

    void OnEnable()
    {
        if (dispatcherRT != null) dispatcherRT.gameObject.SetActive(true);
    }

    void OnDisable()
    {
        if (dispatcherRT != null) dispatcherRT.gameObject.SetActive(false);
    }

    void OnDestroy()
    {
        if (dispatcherRT != null)
        {
            try { Destroy(dispatcherRT.gameObject); } catch { }
            dispatcherRT = null;
            dispatcherComp = null;
        }
        if (tempSalvageParent != null)
        {
            try { Destroy(tempSalvageParent.gameObject); } catch { }
            tempSalvageParent = null;
        }
    }

    void Update()
    {
        // auto rebuild detection
        if (autoRebuildOnSizeChange)
        {
            RectTransform boundsRT = (boundsContainerRef != null) ? boundsContainerRef : rt;
            Vector2 curBoundsSize = boundsRT.rect.size;
            float curCanvasScale = (rootCanvas != null) ? rootCanvas.scaleFactor : 1f;
            if (HasLayoutChanged(curBoundsSize, curCanvasScale))
            {
                BuildMenu();
                CacheCurrentLayoutState();
            }
        }

        // selection follower rotation (optional)
        if (selectionFollowerContainer != null)
        {
            bool joystickMoved = false;
            float rawAngleDeg = float.NaN;
            if (!useGamepad)
            {
                Camera cam = (rootCanvas != null && rootCanvas.renderMode != RenderMode.ScreenSpaceOverlay) ? rootCanvas.worldCamera : null;
                Vector2 localPoint;
                RectTransform boundsRT_local = (boundsContainerRef != null) ? boundsContainerRef : rt;
                bool valid = RectTransformUtility.ScreenPointToLocalPointInRectangle(boundsRT_local, Input.mousePosition, cam, out localPoint);
                if (!valid)
                    rawAngleDeg = Mathf.Atan2(Input.mousePosition.y - rt.position.y, Input.mousePosition.x - rt.position.x) * Mathf.Rad2Deg;
                else
                    rawAngleDeg = Mathf.Atan2(localPoint.y, localPoint.x) * Mathf.Rad2Deg;
            }
            else
            {
                float hx = Input.GetAxis("Horizontal");
                float vy = Input.GetAxis("Vertical");
                joystickMoved = new Vector2(hx, vy).magnitude > gamepadDeadzone;
                if (joystickMoved) rawAngleDeg = Mathf.Atan2(vy, hx) * Mathf.Rad2Deg;
            }

            if (!float.IsNaN(rawAngleDeg))
            {
                if (!useGamepad || joystickMoved)
                    selectionFollowerContainer.rotation = Quaternion.Euler(0, 0, rawAngleDeg + 270f);
            }
        }
    }

    /// <summary>
    /// Build or rebuild the menu.
    /// All angle/radius calculations used by pointer tests are done in boundsRT local coordinates.
    /// The visible slot count equals baseCount + extraSlotsFromHoldings.
    /// </summary>
    public void BuildMenu()
    {
#if UNITY_EDITOR
        // Avoid creating runtime objects from OnValidate in editor mode
        if (!Application.isPlaying && !UnityEditor.EditorApplication.isPlayingOrWillChangePlaymode)
        {
            rt = GetComponent<RectTransform>();
            return;
        }
#endif
        // Clear and salvage existing slots (if any)
        ClearGenerated();

        slotCenterAngles.Clear();
        slotBoundaryNext.Clear();

        // base count (items list takes precedence else slotCount)
        int baseCount = (items != null && items.Count > 0) ? items.Count : Mathf.Max(1, slotCount);
        // visible count is base + holdings
        int visibleCount = Mathf.Max(1, baseCount + Mathf.Max(0, extraSlotsFromHoldings));

        // compute slice angle using visibleCount
        float sliceAngle = totalAngle / Mathf.Max(1, visibleCount);
        cachedSliceAngle = sliceAngle;
        float effectiveSliceAngle = Mathf.Max(0.1f, sliceAngle - Mathf.Clamp(sliceGapDegrees, 0f, sliceAngle * 0.9f));
        float halfEffectiveRad = Mathf.Deg2Rad * effectiveSliceAngle * 0.5f;
        float startAngle = globalOffset - totalAngle * 0.5f;

        // measure base prefab size
        float baseWidth = 200f, baseHeight = 200f;
        RectTransform prefabRT = null;
        if (buttonPrefab != null)
        {
            prefabRT = buttonPrefab.GetComponent<RectTransform>();
            if (prefabRT != null) { baseWidth = Mathf.Max(1f, prefabRT.rect.width); baseHeight = Mathf.Max(1f, prefabRT.rect.height); }
        }
        else if (items != null && items.Count > 0 && items[0] != null)
        {
            prefabRT = items[0].GetComponent<RectTransform>();
            if (prefabRT != null) { baseWidth = Mathf.Max(1f, prefabRT.rect.width); baseHeight = Mathf.Max(1f, prefabRT.rect.height); }
        }

        RectTransform boundsRT = (boundsContainerRef != null) ? boundsContainerRef : rt;
        Rect bRect = boundsRT.rect;
        float minSide = Mathf.Min(bRect.width, bRect.height);

        float desiredRadius = useAbsoluteRadius ? Mathf.Clamp(absoluteRadius, 1f, Mathf.Max(1f, minSide * 0.5f - radiusPadding))
                                                : Mathf.Clamp(minSide * radiusPercent * 0.5f, 1f, Mathf.Max(1f, minSide * 0.5f - radiusPadding));

        float maxAllowedRadius = Mathf.Max(1f, (minSide * 0.5f) - (Mathf.Max(baseWidth, baseHeight) * 0.5f) - radiusPadding);
        float finalRadius = Mathf.Min(desiredRadius, maxAllowedRadius);
        if (finalRadius < 1f) finalRadius = Mathf.Max(1f, baseWidth / (2f * Mathf.Max(0.0001f, Mathf.Sin(halfEffectiveRad))));

        float wMax = 2f * finalRadius * Mathf.Max(0.0001f, Mathf.Sin(halfEffectiveRad)) - radiusPadding;
        float scaleFactor = 1f;
        if (fitToBounds)
        {
            scaleFactor = (wMax <= 1f) ? minScale : Mathf.Clamp(wMax / baseWidth, minScale, maxScale);
        }
        scaleFactor = Mathf.Clamp(scaleFactor * prefabScaleMultiplier, minScale, maxScale);

        // choose parent target for instantiated slots (slots themselves still live under parentTarget)
        Transform parentTarget = (slotParentContainer != null) ? slotParentContainer : rt;
        RectTransform parentRT = parentTarget as RectTransform;

        // instantiate visible slices and compute center angles using boundsRT local coordinates (posInBounds)
        for (int i = 0; i < visibleCount; i++)
        {
            // compute raw center angle for this slice (without visual-only rotation yet)
            float centerAngleDeg = startAngle + sliceAngle * i + sliceAngle * 0.5f;

            // Apply rotationOffset to the center used for selection & placement if desired.
            float usedCenterAngleDeg = centerAngleDeg;
            if (rotationOffsetAffectsSelection)
                usedCenterAngleDeg += rotationOffset; // apply rotationOffset to angle used in selection math

            float centerAngleRad = usedCenterAngleDeg * Mathf.Deg2Rad;

            GameObject go;
            // if user provided items list, try to reuse/inherit; if visibleCount > items.Count we still instantiate clones of last prefab
            if (items != null && items.Count > 0)
            {
                int sourceIndex = i % items.Count;
                var original = items[sourceIndex];
                if (original == null)
                {
                    // if missing, skip
                    continue;
                }
                go = (original.transform.parent == parentTarget) ? original : Instantiate(original, parentTarget, false);
            }
            else
            {
                if (buttonPrefab == null) return;
                go = Instantiate(buttonPrefab, parentTarget, false);
            }

            RectTransform slotRT = go.GetComponent<RectTransform>();
            if (slotRT == null)
            {
                Debug.LogError("Radial Menu: button prefab/item must have a RectTransform.");
                if (go != null && go.name.Contains("(Clone)")) Destroy(go);
                continue;
            }

            Vector3 baseScale = Vector3.one * scaleFactor;
            Image slotImg = go.GetComponent<Image>();
            if (slotImg == null)
            {
                float gapRatio = (sliceGapDegrees / Mathf.Max(0.0001f, sliceAngle));
                float shrinkFactor = Mathf.Clamp01(1f - gapRatio * 0.5f);
                baseScale *= shrinkFactor;
            }

            slotRT.localScale = baseScale;

            // posInBounds is in boundsRT local coordinates (x,y)
            Vector2 posInBounds = new Vector2(Mathf.Cos(centerAngleRad), Mathf.Sin(centerAngleRad)) * finalRadius;

            // place visually under parentTarget (convert world)
            if (parentRT != null)
            {
                Vector3 worldPos = boundsRT.TransformPoint(posInBounds);
                Vector3 parentLocalPos = parentRT.InverseTransformPoint(worldPos);
                slotRT.localPosition = new Vector3(parentLocalPos.x, parentLocalPos.y, 0f);
            }
            else
            {
                Vector3 lp = new Vector3(posInBounds.x, posInBounds.y, 0f);
                slotRT.localPosition = lp;
            }

            // visual rotation: always apply rotationOffset to visuals so artwork aligns with intended rotation.
            float visualCenterAngleDeg = centerAngleDeg + rotationOffset;
            float startEdgeAngleForEffective = visualCenterAngleDeg - (effectiveSliceAngle * 0.5f);
            slotRT.localEulerAngles = new Vector3(0, 0, startEdgeAngleForEffective);

            if (slotImg != null)
            {
                slotImg.type = Image.Type.Filled;
                slotImg.fillMethod = Image.FillMethod.Radial360;
                slotImg.fillAmount = Mathf.Clamp01(effectiveSliceAngle / 360f);
            }

            if (keepChildrenUpright)
            {
                for (int c = 0; c < slotRT.childCount; c++)
                {
                    var child = slotRT.GetChild(c) as RectTransform;
                    if (child != null) child.localEulerAngles = Vector3.zero;
                }
            }

            instantiatedSlots.Add(slotRT);
            slotBaseScales.Add(baseScale);

            // --- compute center angle from boundsRT local position (posInBounds)
            float angleFromBoundsLocal = Mathf.Atan2(posInBounds.y, posInBounds.x) * Mathf.Rad2Deg;
            if (angleFromBoundsLocal < 0f) angleFromBoundsLocal += 360f;
            slotCenterAngles.Add(angleFromBoundsLocal);
        }

        // compute sector boundaries (midpoint between each center and next center) based on visible slots
        int n = slotCenterAngles.Count;
        slotBoundaryNext.Clear();
        if (n > 0)
        {
            for (int i = 0; i < n; i++)
            {
                float a = slotCenterAngles[i];
                float b = slotCenterAngles[(i + 1) % n];
                float halfDelta = Mathf.DeltaAngle(a, b) * 0.5f;
                float mid = a + halfDelta;
                mid = (mid % 360f + 360f) % 360f;
                slotBoundaryNext.Add(mid);
            }
        }

        // cached final radius in boundsRT local units (use finalRadius + half prefab size)
        cachedFinalRadius = finalRadius + (Mathf.Max(baseWidth, baseHeight) * 0.5f * scaleFactor) + radiusPadding;
        if (cachedFinalRadius <= 0f) cachedFinalRadius = finalRadius;
        cachedInnerRadius = Mathf.Clamp01(innerHoleScale) * cachedFinalRadius;

        // create/update dispatcher parented to chosen container
        CreateOrUpdateDispatcher(parentRT, boundsRT, cachedFinalRadius, cachedInnerRadius, sliceAngle, instantiatedSlots.Count, baseHeight, scaleFactor);

        int previousCount = cachedCount;
        cachedCount = instantiatedSlots.Count;
        cachedBoundsRT = boundsRT;

        // if count changed (or if you always want it) print the dump
        if (debugLogs)
        {
            if (previousCount != cachedCount)
            {
                Debug.LogFormat("[RMF] Slot count changed: {0} -> {1}. Dumping angles...", previousCount, cachedCount);
                DumpSlotAngles();
            }
        }

        // restore any salvaged draggables that were temporarily moved away
        if (enableSalvage && salvagedDraggables != null && salvagedDraggables.Count > 0)
        {
            RestoreSalvagedDraggables();
        }

        currentSelected = -1;
        UpdateSelectionVisuals(-1);

        // Re-apply outlines for enhanced neighbor slots if any (so rebuild preserves outlines)
        ReapplyEnhanceOutlinesAfterRebuild();
    }

    private void CreateOrUpdateDispatcher(RectTransform parentRT, RectTransform boundsRT, float finalRadiusComputed, float innerRadiusComputed, float sliceAngle, int count, float baseHeight, float scaleFactor)
    {
        RectTransform desiredParentRect = dispatcherParentContainer as RectTransform;
        if (desiredParentRect == null) desiredParentRect = parentRT != null ? parentRT : rt;

        if (dispatcherRT == null)
        {
            if (squareButtonPrefab == null)
            {
                if (debugLogs) Debug.LogWarning("Radial Menu: squareButtonPrefab is not assigned — dispatcher won't be created.");
                return;
            }

            GameObject go = Instantiate(squareButtonPrefab, desiredParentRect != null ? (Transform)desiredParentRect : (Transform)rt, false);
            go.name = "RadialHitboxDispatcher_" + name;
            dispatcherRT = go.GetComponent<RectTransform>() ?? go.AddComponent<RectTransform>();
            dispatcherComp = go.GetComponent<RadialHitboxDispatcher>() ?? go.AddComponent<RadialHitboxDispatcher>();
            dispatcherComp.owner = this;
        }
        else
        {
            Transform desiredParentTransform = desiredParentRect != null ? (Transform)desiredParentRect : (Transform)rt;
            if (dispatcherRT.parent != desiredParentTransform)
                dispatcherRT.SetParent(desiredParentTransform, false);
        }

        dispatcherRT.anchorMin = dispatcherRT.anchorMax = new Vector2(0.5f, 0.5f);
        dispatcherRT.pivot = new Vector2(0.5f, 0.5f);
        dispatcherRT.localRotation = Quaternion.identity;
        dispatcherRT.localScale = Vector3.one;

        if (desiredParentRect != null && boundsRT != null)
        {
            var relBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(desiredParentRect, boundsRT);
            Vector3 centerLocal = relBounds.center;
            Vector2 sizeLocal = new Vector2(relBounds.size.x, relBounds.size.y);
            sizeLocal += new Vector2(hitboxOuterPadding * 2f, hitboxOuterPadding * 2f);
            dispatcherRT.sizeDelta = sizeLocal;
            dispatcherRT.localPosition = centerLocal;
            dispatcherRT.anchoredPosition = new Vector2(centerLocal.x, centerLocal.y);
        }
        else
        {
            float fallback = (boundsRT != null) ? Mathf.Max(boundsRT.rect.width, boundsRT.rect.height) : 200f;
            dispatcherRT.sizeDelta = new Vector2(fallback + hitboxOuterPadding * 2f, fallback + hitboxOuterPadding * 2f);
            dispatcherRT.localPosition = Vector3.zero;
            dispatcherRT.anchoredPosition = Vector2.zero;
        }

        Image img = dispatcherRT.GetComponent<Image>();
        if (img == null) img = dispatcherRT.gameObject.AddComponent<Image>();
        img.raycastTarget = true;

        dispatcherRT.SetAsLastSibling();

        if (debugLogs) Debug.LogFormat("[RMF] Dispatcher created/updated: parent='{0}', localPos={1}, size={2}",
            dispatcherRT.parent != null ? dispatcherRT.parent.name : "(null)",
            dispatcherRT.localPosition, dispatcherRT.sizeDelta);
    }

    /// <summary>
    /// Public: called by RadialHitboxDispatcher when a Holding item was placed into the radial menu.
    /// Adds one visible slot and rebuilds the menu.
    /// </summary>
    public void AddHoldingSlot()
    {
        extraSlotsFromHoldings++;
        if (debugLogs) Debug.LogFormat("[RMF] AddHoldingSlot called. extraSlotsFromHoldings={0}", extraSlotsFromHoldings);
        BuildMenu();
        TryNotifyMenuHandlerUpdateSpells();
    }

    /// <summary>
    /// Public: called by RadialHitboxDispatcher when a Holding item was removed from the radial menu.
    /// Removes one visible slot (not going below zero) and rebuilds the menu.
    /// </summary>
    public void RemoveHoldingSlot()
    {
        extraSlotsFromHoldings = Mathf.Max(0, extraSlotsFromHoldings - 1);
        if (debugLogs) Debug.LogFormat("[RMF] RemoveHoldingSlot called. extraSlotsFromHoldings={0}", extraSlotsFromHoldings);
        BuildMenu();
        TryNotifyMenuHandlerUpdateSpells();
    }

    /// <summary>
    /// Public helper used by the dispatcher to determine if a given slot GameObject is the last visible slot.
    /// Returns true if the provided slotGO matches the last element in instantiatedSlots.
    /// </summary>
    public bool IsLastVisibleSlot(GameObject slotGO)
    {
        if (slotGO == null) return false;
        if (instantiatedSlots == null || instantiatedSlots.Count == 0) return false;
        var last = instantiatedSlots[instantiatedSlots.Count - 1];
        if (last == null) return false;
        return slotGO == last.gameObject;
    }

    /// <summary>
    /// Before we destroy slot objects, move their Draggable children to a temporary salvage parent
    /// so they aren't lost when we Destroy(slot). The temporary parent lives under this menu's parent
    /// so it won't be destroyed by ClearGenerated's slot destruction.
    /// </summary>
    private void PrepareTemporarySalvageParent()
    {
        if (tempSalvageParent != null) return;

        // Prefer to create the temp object under the same parent as slotParentContainer (or this rt)
        Transform putUnder = slotParentContainer != null ? slotParentContainer.parent : rt.parent;
        if (putUnder == null) putUnder = rt;

        GameObject tmp = new GameObject("__RMF_SalvageTemp_" + name);
        tmp.hideFlags = HideFlags.HideAndDontSave;
        tmp.transform.SetParent(putUnder, false);
        tempSalvageParent = tmp.transform;
    }

    private void ClearTemporarySalvageParent()
    {
        if (tempSalvageParent != null)
        {
            try { Destroy(tempSalvageParent.gameObject); } catch { }
            tempSalvageParent = null;
        }
    }

    private void ClearGenerated()
    {
        // SALVAGE: temporarily extract draggables from slot hierarchy to avoid losing them when slots are destroyed
        salvagedDraggables.Clear();
        if (enableSalvage && instantiatedSlots != null && instantiatedSlots.Count > 0)
        {
            PrepareTemporarySalvageParent();

            foreach (var rtSlot in instantiatedSlots)
            {
                if (rtSlot == null) continue;

                // find Draggable components in descendants
                var found = rtSlot.GetComponentsInChildren<Draggable>(true);
                if (found == null || found.Length == 0) continue;

                foreach (var d in found)
                {
                    if (d == null) continue;
                    // avoid double-adding
                    if (salvagedDraggables.Contains(d)) continue;

                    // Reparent the draggable to the temp salvage parent while keeping world position/rotation
                    try
                    {
                        Transform dt = d.transform;
                        dt.SetParent(tempSalvageParent, true);
                        salvagedDraggables.Add(d);
                        if (debugLogs) Debug.LogFormat("[RMF] Temporarily salvaged '{0}' from slot '{1}'", d.name, rtSlot.name);
                    }
                    catch (Exception ex)
                    {
                        if (debugLogs) Debug.LogException(ex);
                    }
                }
            }
        }

        // Now safe to destroy clone slots (draggables have been moved)
        for (int i = 0; i < instantiatedSlots.Count; i++)
        {
            var rtSlot = instantiatedSlots[i];
            if (rtSlot == null) continue;
            if (rtSlot.gameObject.name.Contains("(Clone)"))
                Destroy(rtSlot.gameObject);
        }
        instantiatedSlots.Clear();
        slotBaseScales.Clear();
        slotCenterAngles.Clear();
        slotBoundaryNext.Clear();

        if (dispatcherRT != null)
        {
            Destroy(dispatcherRT.gameObject);
            dispatcherRT = null;
            dispatcherComp = null;
        }
    }

    /// <summary>
    /// Restore salvaged draggables after new slots are created.
    /// Attempts to place each draggable into the nearest empty persistent slot; if none, uses salvageParent/fallback.
    /// Preserves world position, rotation and visual scale.
    /// </summary>
    private void RestoreSalvagedDraggables()
    {
        if (salvagedDraggables == null || salvagedDraggables.Count == 0)
        {
            ClearTemporarySalvageParent();
            return;
        }

        // Build list of available slots: prefer empty slots
        List<RectTransform> availableSlots = new List<RectTransform>();
        foreach (var s in instantiatedSlots)
        {
            if (s == null) continue;
            if (s.childCount == 0)
                availableSlots.Add(s);
        }

        // fallback parent (allowed to already have children)
        Transform fallbackParent = salvageParent != null ? salvageParent : (slotParentContainer != null ? slotParentContainer : rt);
        if (fallbackParent == null) fallbackParent = rt;

        // Pre-cache some data: ensure cachedBoundsRT is valid (we computed slotCenterAngles in bounds local space)
        if (cachedBoundsRT == null)
        {
            cachedBoundsRT = (boundsContainerRef != null) ? boundsContainerRef : rt;
        }

        // For each salvaged draggable try to assign to nearest available slot (angular distance), else put under fallbackParent
        foreach (var d in new List<Draggable>(salvagedDraggables))
        {
            if (d == null) continue;
            Transform dt = d.transform;

            // snapshot world transform
            Vector3 worldPos = dt.position;
            Quaternion worldRot = dt.rotation;
            Vector3 worldScale = dt.lossyScale;

            RectTransform nearest = null;
            float bestAngularDist = float.MaxValue;

            // compute draggable angle in boundsRT local space (same space used for slotCenterAngles)
            float draggableAngle = 0f;
            bool haveAngle = false;
            try
            {
                if (cachedBoundsRT != null)
                {
                    Vector2 localPoint = cachedBoundsRT.InverseTransformPoint(worldPos);
                    float ang = Mathf.Atan2(localPoint.y, localPoint.x) * Mathf.Rad2Deg;
                    if (ang < 0f) ang += 360f;
                    draggableAngle = ang;
                    haveAngle = true;
                }
            }
            catch { haveAngle = false; }

            if (haveAngle && availableSlots.Count > 0)
            {
                // choose slot by smallest absolute angular delta to slotCenterAngles
                foreach (var cand in availableSlots)
                {
                    if (cand == null) continue;
                    int idx = instantiatedSlots.IndexOf(cand);
                    if (idx < 0 || idx >= slotCenterAngles.Count) continue;
                    float slotAng = slotCenterAngles[idx];
                    float delta = Mathf.Abs(Mathf.DeltaAngle(draggableAngle, slotAng));
                    if (delta < bestAngularDist)
                    {
                        bestAngularDist = delta;
                        nearest = cand;
                    }
                }
            }

            Transform targetParent = (nearest != null) ? (Transform)nearest : fallbackParent;

            try
            {
                if (salvageMode == SalvageMode.Move)
                {
                    // reparent while preserving world pos/rot (SetParent with worldPositionStays = true)
                    dt.SetParent(targetParent, true);

                    // Re-apply world position/rotation to be sure
                    dt.position = worldPos;
                    dt.rotation = worldRot;

                    // compute localScale required to give desired worldScale under new parent
                    Vector3 parentLossy = dt.parent ? dt.parent.lossyScale : Vector3.one;
                    Vector3 desiredLocal = new Vector3(
                        worldScale.x / Mathf.Max(1e-6f, parentLossy.x),
                        worldScale.y / Mathf.Max(1e-6f, parentLossy.y),
                        worldScale.z / Mathf.Max(1e-6f, parentLossy.z)
                    );
                    dt.localScale = desiredLocal;

                    // update Draggable.parentAfterDrag if possible
                    try { d.parentAfterDrag = targetParent; } catch { }

                    if (debugLogs) Debug.LogFormat("[RMF] Restored salvaged '{0}' into '{1}' (angularDist={2})", d.name, targetParent.name, (nearest != null ? bestAngularDist.ToString("F2") : "fallback"));
                }
                else // Duplicate
                {
                    GameObject clone = Instantiate(dt.gameObject);
                    clone.transform.SetParent(targetParent, true);
                    clone.transform.position = worldPos;
                    clone.transform.rotation = worldRot;

                    Vector3 parentLossy = clone.transform.parent ? clone.transform.parent.lossyScale : Vector3.one;
                    Vector3 desiredLocal = new Vector3(
                        worldScale.x / Mathf.Max(1e-6f, parentLossy.x),
                        worldScale.y / Mathf.Max(1e-6f, parentLossy.y),
                        worldScale.z / Mathf.Max(1e-6f, parentLossy.z)
                    );
                    clone.transform.localScale = desiredLocal;

                    var cloneDr = clone.GetComponent<Draggable>() ?? clone.GetComponentInChildren<Draggable>();
                    if (cloneDr != null)
                    {
                        try { cloneDr.parentAfterDrag = targetParent; } catch { }
                    }

                    if (debugLogs) Debug.LogFormat("[RMF] Restored (clone) salvaged '{0}' into '{1}'", clone.name, targetParent.name);
                }

                // remove nearest from availability list (if used)
                if (nearest != null)
                    availableSlots.Remove(nearest);
            }
            catch (Exception ex)
            {
                if (debugLogs) Debug.LogException(ex);
            }
        }

        // After restore, clear list and destroy temp container
        salvagedDraggables.Clear();
        ClearTemporarySalvageParent();

        // --- NEW: notify MenuHandler so updateSpells runs after salvage ---
        TryNotifyMenuHandlerUpdateSpells();
    }

    private void TryNotifyMenuHandlerUpdateSpells()
    {
        if (menuHandler == null)
        {
            // try parent first
            menuHandler = GetComponentInParent<MenuHandler>();
            if (menuHandler == null)
            {
                // fallback: find any in scene (last resort)
                menuHandler = FindObjectOfType<MenuHandler>();
            }
        }

        if (menuHandler != null)
        {
            try
            {
                menuHandler.updateSpells();
                if (debugLogs) Debug.Log("[RMF] Notified MenuHandler.updateSpells() after salvage.");
            }
            catch (Exception ex)
            {
                if (debugLogs) Debug.LogException(ex);
            }
        }
        else
        {
            if (debugLogs) Debug.LogWarning("[RMF] No MenuHandler found to call updateSpells() after salvage. Assign one in the inspector if needed.");
        }
    }

    private bool AngleInsideSector(float angle, float sectorStart, float sectorEnd)
    {
        angle = (angle % 360f + 360f) % 360f;
        sectorStart = (sectorStart % 360f + 360f) % 360f;
        sectorEnd = (sectorEnd % 360f + 360f) % 360f;

        float span = (sectorEnd - sectorStart + 360f) % 360f;
        float pos = (angle - sectorStart + 360f) % 360f;
        return pos <= span;
    }

    /// <summary>
    /// Called by dispatcher when the pointer moves / hovers inside dispatcher.
    /// </summary>
    public void ProcessPointer(Vector2 screenPoint)
    {
        if (cachedCount <= 0 || cachedBoundsRT == null) return;

        Canvas c = cachedBoundsRT.GetComponentInParent<Canvas>();
        Camera cam = (c != null && c.renderMode != RenderMode.ScreenSpaceOverlay) ? c.worldCamera : null;

        Vector2 local;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(cachedBoundsRT, screenPoint, cam, out local)) return;

        float r = local.magnitude;
        if (r < cachedInnerRadius || r > (cachedFinalRadius + hitboxOuterPadding))
        {
            if (currentSelected != -1)
            {
                currentSelected = -1;
                UpdateSelectionVisuals(-1);
                onSelectionChanged?.Invoke(-1, null);
            }
            return;
        }

        float ang = Mathf.Atan2(local.y, local.x) * Mathf.Rad2Deg;
        if (ang < 0f) ang += 360f;

        int found = -1;
        int n = slotCenterAngles.Count;
        for (int i = 0; i < n; i++)
        {
            float boundaryPrev = slotBoundaryNext[(i - 1 + n) % n];
            float boundaryNext = slotBoundaryNext[i];
            if (AngleInsideSector(ang, boundaryPrev, boundaryNext))
            {
                // SHIFT: map this sector to the NEXT slot (i+1)
                found = (i + 1) % n;
                break;
            }
        }

        if (debugLogs) Debug.LogFormat("[RMF] ProcessPointer called with screenPoint {0} ang={1} found={2}", screenPoint, ang, found);

        if (found != currentSelected)
        {
            currentSelected = found;
            UpdateSelectionVisuals(currentSelected);
            onSelectionChanged?.Invoke(currentSelected, (currentSelected >= 0 ? instantiatedSlots[currentSelected] : null));
        }
    }

    /// <summary>
    /// Called by dispatcher when a click happens.
    /// </summary>
    public void ProcessClick(Vector2 screenPoint)
    {
        if (debugLogs) Debug.LogFormat("[RMF] ProcessClick called with screenPoint {0}", screenPoint);

        if (cachedCount <= 0 || cachedBoundsRT == null) return;

        Canvas c = cachedBoundsRT.GetComponentInParent<Canvas>();
        Camera cam = (c != null && c.renderMode != RenderMode.ScreenSpaceOverlay) ? c.worldCamera : null;

        Vector2 local;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(cachedBoundsRT, screenPoint, cam, out local))
        {
            if (debugLogs) Debug.Log("[RMF] ProcessClick: ScreenPointToLocalPointInRectangle returned false");
            return;
        }
        float r = local.magnitude;

        if (debugLogs) Debug.LogFormat("[RMF] ProcessClick: local={0} r={1} inner={2} finalRadius+pad={3}", local, r, cachedInnerRadius, cachedFinalRadius + hitboxOuterPadding);

        if (r < cachedInnerRadius) { if (debugLogs) Debug.Log("[RMF] inside inner hole"); return; }
        if (r > (cachedFinalRadius + hitboxOuterPadding)) { if (debugLogs) Debug.Log("[RMF] outside final radius+pad"); return; }

        float ang = Mathf.Atan2(local.y, local.x) * Mathf.Rad2Deg;
        if (ang < 0f) ang += 360f;

        int found = -1;
        int n = slotCenterAngles.Count;
        for (int i = 0; i < n; i++)
        {
            float boundaryPrev = slotBoundaryNext[(i - 1 + n) % n];
            float boundaryNext = slotBoundaryNext[i];
            if (AngleInsideSector(ang, boundaryPrev, boundaryNext))
            {
                // SHIFT: map this sector to the NEXT slot (i+1)
                found = (i + 1) % n;
                break;
            }
        }

        if (debugLogs) Debug.LogFormat("[RMF] ProcessClick computed ang={0} found={1}", ang, found);

        if (found >= 0)
        {
            currentSelected = found;
            UpdateSelectionVisuals(currentSelected);
            onSelectionChanged?.Invoke(currentSelected, (currentSelected >= 0 ? instantiatedSlots[currentSelected] : null));
            ForwardClickToSlot(currentSelected, screenPoint);
        }
        else
        {
            if (debugLogs) Debug.Log("[RMF] click not in any sector");
        }
    }

    private void ForwardClickToSlot(int index, Vector2 screenPoint)
    {
        if (index < 0 || index >= instantiatedSlots.Count) return;
        GameObject target = instantiatedSlots[index].gameObject;
        if (target == null) return;
        if (EventSystem.current == null) return;

        var pe = new PointerEventData(EventSystem.current)
        {
            position = screenPoint,
            clickCount = 1,
            button = PointerEventData.InputButton.Left
        };

        bool invoked = ExecuteEvents.Execute(target, pe, ExecuteEvents.pointerClickHandler);
        if (!invoked) invoked = ExecuteEvents.ExecuteHierarchy(target, pe, ExecuteEvents.pointerClickHandler);

        if (!invoked)
        {
            var comps = target.GetComponents<MonoBehaviour>();
            foreach (var c in comps)
            {
                if (c == null) continue;
                if (c is IPointerClickHandler ph)
                {
                    try { ph.OnPointerClick(pe); invoked = true; break; }
                    catch (Exception ex) { Debug.LogException(ex); }
                }
            }
        }

        if (!invoked)
        {
            for (int ci = 0; ci < target.transform.childCount && !invoked; ci++)
            {
                var child = target.transform.GetChild(ci).gameObject;
                var childComps = child.GetComponents<MonoBehaviour>();
                foreach (var mb in childComps)
                {
                    if (mb == null) continue;
                    if (mb is IPointerClickHandler ph)
                    {
                        try { ph.OnPointerClick(pe); invoked = true; break; } catch (Exception ex) { Debug.LogException(ex); }
                    }
                }
            }
        }

        if (!invoked)
        {
            var btn = target.GetComponentInChildren<Button>();
            if (btn != null) { btn.onClick.Invoke(); invoked = true; }
        }

        if (debugLogs) Debug.LogFormat("[RMF] Forwarded click to slot {0} ('{1}') handled={2}", index, target.name, invoked);
    }

    /// <summary>
    /// Clear pointer selection (called by dispatcher on pointer exit)
    /// </summary>
    public void ClearPointer()
    {
        if (currentSelected != -1)
        {
            currentSelected = -1;
            UpdateSelectionVisuals(-1);
            onSelectionChanged?.Invoke(-1, null);
        }
    }

    private void UpdateSelectionVisuals(int selected)
    {
        for (int i = 0; i < instantiatedSlots.Count; i++)
        {
            var rtSlot = instantiatedSlots[i];
            if (rtSlot == null) continue;
            Vector3 baseScale = (i < slotBaseScales.Count) ? slotBaseScales[i] : Vector3.one;
            if (enableHighlight)
            {
                float factor = (i == selected) ? highlightScale : 1f;
                rtSlot.localScale = baseScale * factor;
            }
            else
            {
                rtSlot.localScale = baseScale;
            }
        }
    }

    private bool HasLayoutChanged(Vector2 currentBoundsSize, float currentCanvasScale)
    {
        if (lastBoundsSize == Vector2.zero && lastScreenW == 0 && lastScreenH == 0)
            return false;
        if (Mathf.Abs(currentBoundsSize.x - lastBoundsSize.x) > boundsChangeThreshold ||
            Mathf.Abs(currentBoundsSize.y - lastBoundsSize.y) > boundsChangeThreshold)
            return true;
        if (!Mathf.Approximately(currentCanvasScale, lastCanvasScale)) return true;
        if (Screen.width != lastScreenW || Screen.height != lastScreenH) return true;
        return false;
    }

    private void CacheCurrentLayoutState()
    {
        RectTransform boundsRT = (boundsContainerRef != null) ? boundsContainerRef : rt;
        lastBoundsSize = boundsRT.rect.size;
        lastCanvasScale = (rootCanvas != null) ? rootCanvas.scaleFactor : 1f;
        lastScreenW = Screen.width;
        lastScreenH = Screen.height;
    }

    /// <summary>
    /// Returns the RectTransform of the slot the menu considers under this screen point (using the same
    /// sector math as ProcessPointer). If updateSelection is true, this also updates currentSelected,
    /// selection visuals, and raises onSelectionChanged (so the menu looks/behaves exactly as if the
    /// pointer moved there).
    /// </summary>
    public RectTransform GetSlotAtScreenPoint(Vector2 screenPoint, bool updateSelection = false)
    {
        if (cachedCount <= 0 || cachedBoundsRT == null) return null;

        Canvas c = cachedBoundsRT.GetComponentInParent<Canvas>();
        Camera cam = (c != null && c.renderMode != RenderMode.ScreenSpaceOverlay) ? c.worldCamera : null;

        Vector2 local;
        if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(cachedBoundsRT, screenPoint, cam, out local))
        {
            // If we couldn't map the screen point into local coords, clear selection if requested and return null
            if (updateSelection && currentSelected != -1)
            {
                currentSelected = -1;
                UpdateSelectionVisuals(-1);
                onSelectionChanged?.Invoke(-1, null);
            }
            return null;
        }

        float r = local.magnitude;
        if (r < cachedInnerRadius || r > (cachedFinalRadius + hitboxOuterPadding))
        {
            if (updateSelection && currentSelected != -1)
            {
                currentSelected = -1;
                UpdateSelectionVisuals(-1);
                onSelectionChanged?.Invoke(-1, null);
            }
            return null;
        }

        float ang = Mathf.Atan2(local.y, local.x) * Mathf.Rad2Deg;
        if (ang < 0f) ang += 360f;

        int found = -1;
        int n = slotCenterAngles.Count;
        for (int i = 0; i < n; i++)
        {
            float boundaryPrev = slotBoundaryNext[(i - 1 + n) % n];
            float boundaryNext = slotBoundaryNext[i];
            if (AngleInsideSector(ang, boundaryPrev, boundaryNext))
            {
                // NOTE: keep the same mapping used by ProcessPointer (maps sector i -> slot (i+1)%n)
                found = (i + 1) % n;
                break;
            }
        }

        RectTransform slotRT = (found >= 0 && found < instantiatedSlots.Count) ? instantiatedSlots[found] : null;

        if (updateSelection)
        {
            if (found != currentSelected)
            {
                currentSelected = found;
                UpdateSelectionVisuals(currentSelected);
                onSelectionChanged?.Invoke(currentSelected, (currentSelected >= 0 ? instantiatedSlots[currentSelected] : null));
            }
        }

        return slotRT;
    }

    /// <summary>
    /// Context-menu callable dump. Right-click component header -> Dump Slot Angles
    /// </summary>
    [ContextMenu("Dump Slot Angles")]
    public void DumpSlotAngles()
    {
        string s = GetSlotAnglesString();
        Debug.Log(s);
    }

    /// <summary>
    /// Returns one long string with a readable dump — easy to copy/paste.
    /// Call this from another script: string dump = rm.GetSlotAnglesString();
    /// </summary>
    public string GetSlotAnglesString()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendFormat("[RMF] DumpSlotAngles: count={0} cachedFinalRadius={1} cachedInnerRadius={2}\n", slotCenterAngles.Count, cachedFinalRadius, cachedInnerRadius);
        for (int i = 0; i < slotCenterAngles.Count; i++)
        {
            float c = slotCenterAngles[i];
            float bNext = slotBoundaryNext.Count > 0 ? slotBoundaryNext[i] : float.NaN;
            string name = (i < instantiatedSlots.Count && instantiatedSlots[i] != null) ? instantiatedSlots[i].name : "(null)";
            sb.AppendFormat("[RMF] Slot {0}: center={1:0.000} boundaryNext={2:0.000} slotName={3}\n", i, c, bNext, name);
        }
        return sb.ToString();
    }

#if UNITY_EDITOR
    void OnValidate()
    {
        // In editor avoid creating runtime objects except when in play mode
        if (!Application.isPlaying)
        {
            if (rt == null) rt = GetComponent<RectTransform>();
            return;
        }

        if (Application.isPlaying && autoRebuildOnSizeChange)
        {
            BuildMenu();
            CacheCurrentLayoutState();
        }
    }
#endif

    void OnDrawGizmos()
    {
        if (!debugDrawGizmos) return;
        if (rt == null) rt = GetComponent<RectTransform>();
        Gizmos.color = Color.green;

        if (slotCenterAngles != null && slotCenterAngles.Count > 0 && cachedBoundsRT != null)
        {
            for (int i = 0; i < slotCenterAngles.Count; i++)
            {
                float ang = slotCenterAngles[i] * Mathf.Deg2Rad;
                Vector3 localPt = new Vector3(Mathf.Cos(ang), Mathf.Sin(ang), 0f) * cachedFinalRadius;
                Vector3 worldA = cachedBoundsRT.TransformPoint(localPt);
                Gizmos.color = Color.yellow;
                Gizmos.DrawSphere(worldA, Mathf.Max(2f, cachedFinalRadius * 0.01f));

                if (slotBoundaryNext != null && slotBoundaryNext.Count == slotCenterAngles.Count)
                {
                    float b = slotBoundaryNext[i] * Mathf.Deg2Rad;
                    Vector3 bLocal = new Vector3(Mathf.Cos(b), Mathf.Sin(b), 0f) * (cachedFinalRadius + 10f);
                    Vector3 worldB = cachedBoundsRT.TransformPoint(bLocal);
                    Gizmos.color = Color.cyan;
                    Gizmos.DrawLine(cachedBoundsRT.TransformPoint(Vector3.zero), worldB);
                }
            }
        }
        else
        {
            // fallback theoretical centers (editor only visualization)
            int count = Mathf.Max(1, slotCount);
            float slice = totalAngle / count;
            float startAngle = globalOffset - totalAngle * 0.5f;
            float r = (rt != null) ? Mathf.Min(rt.rect.width, rt.rect.height) * 0.4f : 80f;
            Vector3 worldCenter = (rt != null) ? rt.transform.TransformPoint(Vector3.zero) : transform.position;
            for (int i = 0; i < count; i++)
            {
                float center = startAngle + slice * i + slice * 0.5f;
                float a = center * Mathf.Deg2Rad;
                Vector3 p = worldCenter + new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0f) * r;
                Gizmos.color = Color.yellow;
                Gizmos.DrawSphere(p, 2f);
            }
        }
    }

    // ----------------- ENHANCE / HEART SUPPORT -----------------
    [Header("Enhance / Outline")]
    [Tooltip("Outline color used when a neighbor slot is enhanced.")]
    public Color enhanceOutlineColor = Color.yellow;
    [Tooltip("Outline distance (effectDistance) applied to the outline component.")]
    public Vector2 enhanceOutlineDistance = new Vector2(3f, 3f);

    // Ref-counted map: slotIndex -> how many Enhances currently cause this slot to be outlined.
    private Dictionary<int, int> enhancedRefCount = new Dictionary<int, int>();

    /// <summary>
    /// Called by dispatcher when an Enhance object is placed into a slot (or dropped into one).
    /// Highlights the two adjacent slots (left/right). Idempotent and reference-counted.
    /// </summary>
    public void OnEnhancePlaced(GameObject enhanceGO)
    {
        if (enhanceGO == null || instantiatedSlots == null || instantiatedSlots.Count == 0) return;

        int idx = FindSlotIndexContaining(enhanceGO);
        if (idx < 0) return;

        int n = instantiatedSlots.Count;
        int left = (idx - 1 + n) % n;
        int right = (idx + 1) % n;

        AddEnhancedRef(left);
        AddEnhancedRef(right);
        SetSlotOutline(left, true);
        SetSlotOutline(right, true);
    }

    /// <summary>
    /// Called by dispatcher when an Enhance object leaves a slot (pickup, drop elsewhere, salvage).
    /// Decrements reference counts and removes outlines when count reaches zero.
    /// </summary>
    public void OnEnhanceRemoved(GameObject enhanceGO)
    {
        if (enhanceGO == null || instantiatedSlots == null || instantiatedSlots.Count == 0) return;

        int idx = FindSlotIndexContaining(enhanceGO);
        if (idx < 0) return;

        int n = instantiatedSlots.Count;
        int left = (idx - 1 + n) % n;
        int right = (idx + 1) % n;

        DecEnhancedRef(left);
        DecEnhancedRef(right);

        if (!IsEnhanced(left)) SetSlotOutline(left, false);
        if (!IsEnhanced(right)) SetSlotOutline(right, false);
    }

    private void AddEnhancedRef(int slotIdx)
    {
        if (!enhancedRefCount.ContainsKey(slotIdx)) enhancedRefCount[slotIdx] = 0;
        enhancedRefCount[slotIdx] = enhancedRefCount[slotIdx] + 1;
    }

    private void DecEnhancedRef(int slotIdx)
    {
        if (!enhancedRefCount.ContainsKey(slotIdx)) return;
        enhancedRefCount[slotIdx] = Mathf.Max(0, enhancedRefCount[slotIdx] - 1);
        if (enhancedRefCount[slotIdx] == 0) enhancedRefCount.Remove(slotIdx);
    }

    private bool IsEnhanced(int slotIdx)
    {
        return enhancedRefCount.ContainsKey(slotIdx) && enhancedRefCount[slotIdx] > 0;
    }

    /// <summary>
    /// Returns true if the provided child GameObject (draggable) is currently in a slot that is outlined
    /// due to an adjacent Enhance. Used by EventClick to determine damage boost, etc.
    /// </summary>
    public bool IsChildInEnhancedSlot(GameObject child)
    {
        if (child == null || instantiatedSlots == null) return false;
        int idx = FindSlotIndexContaining(child);
        if (idx < 0) return false;
        return IsEnhanced(idx);
    }

    /// <summary>
    /// Find which visible slot index (instantiatedSlots list) contains the given GameObject (or one of its parents).
    /// Returns -1 if not found.
    /// </summary>
    private int FindSlotIndexContaining(GameObject go)
    {
        if (go == null || instantiatedSlots == null) return -1;
        for (int i = 0; i < instantiatedSlots.Count; i++)
        {
            var s = instantiatedSlots[i];
            if (s == null) continue;
            if (go.transform.IsChildOf(s.transform) || go == s.gameObject) return i;
        }
        return -1;
    }

    /// <summary>
    /// Add or remove a visual Outline on the slot (uses the Image component if present).
    /// </summary>
    private void SetSlotOutline(int slotIdx, bool on)
    {
        if (instantiatedSlots == null) return;
        if (slotIdx < 0 || slotIdx >= instantiatedSlots.Count) return;
        var rt = instantiatedSlots[slotIdx];
        if (rt == null) return;

        // Prefer Image on the slot itself; if not present, check children.
        Image img = rt.GetComponent<Image>() ?? rt.GetComponentInChildren<Image>();
        if (img == null) return;

        // If turning on and outline is missing — add and configure
        var outline = img.GetComponent<Outline>();
        if (on)
        {
            if (outline == null) outline = img.gameObject.AddComponent<Outline>();
            outline.effectColor = enhanceOutlineColor;
            outline.effectDistance = enhanceOutlineDistance;
            outline.useGraphicAlpha = true;
        }
        else
        {
            if (outline != null) Destroy(outline);
        }
    }

    // Reapply outlines after BuildMenu (useful when menu is rebuilt in inspector / runtime)
    private void ReapplyEnhanceOutlinesAfterRebuild()
    {
        if (enhancedRefCount == null || enhancedRefCount.Count == 0) return;
        var keys = new List<int>(enhancedRefCount.Keys);
        foreach (var k in keys)
        {
            if (k >= 0 && k < instantiatedSlots.Count)
                SetSlotOutline(k, true);
        }
    }

    // ---------------- Heart forwarding helpers ----------------

    /// <summary>
    /// Dispatcher calls this when a Heart is put into a slot.
    /// Forward the event to PlayerController (global search).
    /// </summary>
    public void OnHeartPlaced(GameObject heartGo)
    {
        var pc = FindObjectOfType<PlayerController>();
        if (pc != null) pc.OnHeartPlaced(heartGo);
    }

    /// <summary>
    /// Dispatcher calls this when a Heart is removed from a slot (pickup, salvage).
    /// Forward the event to PlayerController (global search).
    /// </summary>
    public void OnHeartRemoved(GameObject heartGo)
    {
        var pc = FindObjectOfType<PlayerController>();
        if (pc != null) pc.OnHeartRemoved(heartGo);
    }
}
