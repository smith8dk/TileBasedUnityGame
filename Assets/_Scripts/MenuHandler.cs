using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System.Collections.Generic;

public class MenuHandler : MonoBehaviour
{
    [Header("Spell Symbol Mapping")]
    [Tooltip("Define mappings from a detected spell name (or substring) to a symbol sprite, color and optional rotation.")]
    public List<SpellMapping> spellMappings = new List<SpellMapping>();

    [Tooltip("If no mapping matches, this sprite will be used for the slot (like your rmThirdImage).")]
    [SerializeField] public Sprite rmThirdImage;
    [Tooltip("Default color applied when no mapping matches (or used alongside mapped color).")]
    public Color defaultColor = Color.white;

    [Header("Symbol Overlay Settings")]
    [Range(0.1f, 1.0f)]
    [Tooltip("What fraction of the slot rect the symbol should occupy (0..1).")]
    public float symbolScalePercent = 0.6f;
    [Tooltip("Pixel offset for symbol positioning (local anchoredPosition).")]
    public Vector2 symbolOffset = Vector2.zero;
    [Tooltip("If true the symbol will be rotated to compensate for the slot's rotation so it appears upright on screen.")]
    public bool compensateForSlotRotation = true;

    [Header("Symbol Options")]
    [Tooltip("If false, symbols will NOT be created/kept as children of the slot (useful to avoid interfering with InventorySlot child-counts).")]
    public bool includeSymbols = true;

    [Header("Slide Settings")]
    [SerializeField] private float slideDistance = 150f; // Distance to slide
    [SerializeField] private float slideDuration = 1f;   // Duration of sliding (in seconds)

    [Header("Linked Objects")]
    [SerializeField] private List<GameObject> secondaryObjects = new List<GameObject>(); // Multiple secondary objects

    [Header("Debug")]
    [Tooltip("Enable verbose debug logs for updateSpells().")]
    public bool enableDebugLogs = true;

    private Vector3 originalPosition;
    private Vector3 targetPosition;

    // Store original & target positions for each secondary object
    private List<Vector3> secondaryOriginalPositions = new List<Vector3>();
    private List<Vector3> secondaryTargetPositions = new List<Vector3>();

    private bool isSlidingRight = false;
    private bool isSliding = false;
    private float slideProgress = 0f; // Progress of the sliding (0 to 1)
    private Coroutine checkChildCountCoroutine;

    [System.Serializable]
    public class SpellMapping
    {
        [Tooltip("String to match against the grandchild name. Use exact name or a substring if matchContains is true.")]
        public string match;

        [Tooltip("If true the script will consider mapping a match when the grandchild name contains the 'match' string.")]
        public bool matchContains = false;

        [Tooltip("Optional symbol shown for this spell. The symbol will be placed as a child named 'Symbol' above the slot background.")]
        public Sprite symbol;

        [Tooltip("Color applied to the slot background (and Symbol image tint if present).")]
        public Color color = Color.white;

        [Tooltip("Rotation (degrees) applied to the symbol Image (Z-euler). Applied after optionally compensating for slot rotation).")]
        public float rotation = 0f;
    }

    private void Awake()
    {
        // Initialize positions
        originalPosition = transform.position;
        targetPosition = originalPosition + new Vector3(slideDistance, 0, 0);

        // Setup secondary objects
        secondaryOriginalPositions.Clear();
        secondaryTargetPositions.Clear();
        foreach (var obj in secondaryObjects)
        {
            if (obj != null)
            {
                secondaryOriginalPositions.Add(obj.transform.position);
                secondaryTargetPositions.Add(obj.transform.position + new Vector3(slideDistance, 0, 0));
            }
            else
            {
                secondaryOriginalPositions.Add(Vector3.zero);
                secondaryTargetPositions.Add(Vector3.zero);
            }
        }

        updateSpells();
    }

    private void OnEnable()
    {
        InventorySlot.OnItemDraggedOrDropped += CheckDirectChildrenCount;
    }

    private void OnDisable()
    {
        InventorySlot.OnItemDraggedOrDropped -= StartCheckDirectChildrenCount;
    }

    private void Update()
    {
        if (isSliding)
        {
            SlideMenu();
        }
        updateSpells();
    }

    private void StartCheckDirectChildrenCount()
    {
        if (checkChildCountCoroutine != null)
        {
            StopCoroutine(checkChildCountCoroutine);
        }
        checkChildCountCoroutine = StartCoroutine(CheckDirectChildrenCountForDuration());
    }

    private IEnumerator CheckDirectChildrenCountForDuration()
    {
        float elapsedTime = 0f;
        while (elapsedTime < 0.1f)
        {
            CheckDirectChildrenCount();
            elapsedTime += Time.deltaTime;
            yield return null;
        }
    }

    public void CheckDirectChildrenCount()
    {
        int directChildCount = transform.childCount;
        if (directChildCount >= 1) // update for any children present
        {
            updateSpells();
        }
    }

    /// <summary>
    /// Update visuals for every direct child slot. The code inspects grandchildren to determine which spell is present,
    /// finds a matching SpellMapping (if any), then sets the slot background color and a symbol overlay (and orientation).
    /// This is run every time you call updateSpells() so it corrects orientation/color live.
    /// </summary>
    public void updateSpells()
    {
        int processCount = transform.childCount; // handle any number of slots

        if (enableDebugLogs)
            Debug.LogFormat("[MenuHandler] updateSpells() called. Processing {0} slot(s).", processCount);

        for (int i = 0; i < processCount; i++)
        {
            Transform slot = transform.GetChild(i);
            if (slot == null) continue;

            if (enableDebugLogs)
                Debug.LogFormat("[MenuHandler] Slot {0}: name='{1}', childCount={2}", i, slot.name, slot.childCount);

            bool mapped = false;

            // examine grandchildren to detect the spell
            for (int j = 0; j < slot.childCount; j++)
            {
                Transform grandChild = slot.GetChild(j);
                if (grandChild == null) continue;

                string gName = grandChild.name ?? "";

                if (enableDebugLogs)
                    Debug.LogFormat("[MenuHandler]  Slot {0} -> checking grandchild {1}: '{2}'", i, j, gName);

                // Try to match against each mapping
                for (int m = 0; m < spellMappings.Count; m++)
                {
                    var map = spellMappings[m];
                    if (map == null || string.IsNullOrEmpty(map.match)) continue;

                    bool isMatch = false;

                    if (!map.matchContains)
                    {
                        if (map.match == gName || map.match == ("rmThird" + gName))
                            isMatch = true;
                    }
                    else
                    {
                        if ((!string.IsNullOrEmpty(gName) && gName.Contains(map.match)) || ("rmThird" + gName).Contains(map.match))
                            isMatch = true;
                    }

                    if (enableDebugLogs)
                        Debug.LogFormat("[MenuHandler]   Trying mapping[{0}] match='{1}' matchContains={2} => isMatch={3}",
                                        m, map.match, map.matchContains, isMatch);

                    if (!isMatch) continue;

                    if (enableDebugLogs)
                    {
                        Debug.LogFormat("[MenuHandler]   Mapping matched for Slot {0} using mapping[{1}] (match='{2}'). Applying visuals: symbol={3}, color={4}, rotation={5}",
                                        i, m, map.match, (map.symbol != null ? map.symbol.name : "null"), map.color, map.rotation);
                    }

                    ApplyMappingToSlotOverlay(slot, map);
                    mapped = true;
                    break;
                }

                if (mapped)
                {
                    if (enableDebugLogs)
                        Debug.LogFormat("[MenuHandler] Slot {0}: mapping applied (from grandchild '{1}').", i, gName);
                    break; // stop checking other grandchildren
                }
            }

            if (!mapped)
            {
                if (enableDebugLogs)
                    Debug.LogFormat("[MenuHandler] Slot {0}: no mapping matched -> applying default visuals (rmThirdImage={1}, defaultColor={2})",
                                    i, (rmThirdImage != null ? rmThirdImage.name : "null"), defaultColor);

                ApplyDefaultVisuals(slot);
            }
        }
    }

    /// <summary>
    /// Ensure there's a child named "Symbol" with an Image above the slot background and apply the mapping there.
    /// The slot background sprite is NOT replaced.
    /// </summary>
    private void ApplyMappingToSlotOverlay(Transform slot, SpellMapping map)
    {
        if (slot == null || map == null) return;

        // apply background tint/color as before
        Image bg = slot.GetComponent<Image>();
        if (bg != null)
        {
            bg.color = map.color;
            if (enableDebugLogs) Debug.LogFormat("[MenuHandler]   Set slot '{0}' background color to {1}", slot.name, map.color);
        }

        // If symbols are disabled globally, ensure no Symbol child exists and return
        if (!includeSymbols)
        {
            // Remove existing Symbol child if present (don't let it count as a slot child)
            Transform existingSymbol = slot.Find("Symbol");
            if (existingSymbol != null)
            {
                Destroy(existingSymbol.gameObject);
                if (enableDebugLogs) Debug.LogFormat("[MenuHandler]   Removed Symbol child from slot '{0}' because includeSymbols=false", slot.name);
            }
            return;
        }

        // find or create Symbol child
        Transform symbolT = slot.Find("Symbol");
        if (symbolT == null)
        {
            GameObject symGo = new GameObject("Symbol", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            symGo.transform.SetParent(slot, false);
            symbolT = symGo.transform;
            if (enableDebugLogs) Debug.LogFormat("[MenuHandler]   Created 'Symbol' child under slot '{0}'", slot.name);
        }

        Image symImg = symbolT.GetComponent<Image>();
        if (symImg == null)
            symImg = symbolT.gameObject.AddComponent<Image>();

        // Always make symbol render on top
        symbolT.SetAsLastSibling();

        // assign sprite & tint
        if (map.symbol != null)
        {
            symImg.enabled = true;
            symImg.sprite = map.symbol;
            symImg.type = Image.Type.Simple;
            symImg.preserveAspect = true;
            symImg.raycastTarget = false;
            symImg.color = Color.white; // let slot color be background color; symbol keeps sprite colors
        }
        else
        {
            // no symbol provided: clear symbol image
            symImg.sprite = null;
            symImg.color = Color.clear;
            symImg.enabled = false;
        }

        // size & placement: center anchored and sized relative to slot rect
        RectTransform slotRT = slot as RectTransform;
        RectTransform symRT = symbolT as RectTransform;

        if (slotRT != null && symRT != null)
        {
            // set anchors/pivot to center so localPosition is expected
            symRT.anchorMin = symRT.anchorMax = new Vector2(0.5f, 0.5f);
            symRT.pivot = new Vector2(0.5f, 0.5f);

            // compute size based on slot rect
            Vector2 slotSize = slotRT.rect.size;
            float scale = Mathf.Clamp(symbolScalePercent, 0.05f, 1f);
            Vector2 symbolSize = slotSize * scale;

            // apply size
            symRT.sizeDelta = symbolSize;

            // set anchored position + optional offset
            symRT.anchoredPosition = symbolOffset;

            // determine rotation: compensate for slot's rotation (if requested) then apply mapping.rotation
            float slotZ = slotRT.localEulerAngles.z;
            float finalRotation = map.rotation;
            if (compensateForSlotRotation)
            {
                // negate slot rotation so symbol appears upright on screen, then add mapping rotation
                finalRotation = -slotZ + map.rotation;
            }
            symRT.localEulerAngles = new Vector3(0f, 0f, finalRotation);

            if (enableDebugLogs)
            {
                Debug.LogFormat("[MenuHandler]   Symbol for slot '{0}' placed size={1}, offset={2}, slotZ={3}, finalRotation={4}",
                                slot.name, symbolSize, symbolOffset, slotZ, finalRotation);
            }
        }
    }

    /// <summary>
    /// Apply default fallback visuals for slots that don't match a mapping.
    /// Keep default background sprite and clear symbol overlay if present.
    /// </summary>
    private void ApplyDefaultVisuals(Transform slot)
    {
        if (slot == null) return;
        Image bg = slot.GetComponent<Image>();
        if (bg != null)
        {
            if (rmThirdImage != null)
            {
                // keep background sprite but only set if not already (optional)
                bg.sprite = rmThirdImage;
                bg.type = Image.Type.Simple;
                bg.preserveAspect = true;
                if (enableDebugLogs) Debug.LogFormat("[MenuHandler]   Slot '{0}' background sprite set to fallback '{1}'", slot.name, rmThirdImage.name);
            }
            bg.color = defaultColor;
            if (enableDebugLogs) Debug.LogFormat("[MenuHandler]   Slot '{0}' color set to default {1}", slot.name, defaultColor);
        }

        // clear symbol overlay if present
        Transform symbolT = slot.Find("Symbol");
        if (symbolT != null)
        {
            Image symImg = symbolT.GetComponent<Image>();
            if (symImg != null)
            {
                symImg.sprite = null;
                symImg.color = Color.clear;
                symImg.rectTransform.localEulerAngles = Vector3.zero;
                symImg.enabled = false;
            }
            // If includeSymbols is false, we remove symbol entirely so it doesn't count as a child
            if (!includeSymbols)
            {
                Destroy(symbolT.gameObject);
                if (enableDebugLogs) Debug.LogFormat("[MenuHandler]   Destroyed 'Symbol' child for slot '{0}' (includeSymbols=false).", slot.name);
            }
            else
            {
                if (enableDebugLogs) Debug.LogFormat("[MenuHandler]   Cleared 'Symbol' child of slot '{0}' (fallback applied).", slot.name);
            }
        }
    }

    public void ToggleSlide()
    {
        isSlidingRight = !isSlidingRight;
        isSliding = true;
        slideProgress = 0f;
    }

    private void SlideMenu()
    {
        slideProgress += Time.deltaTime / slideDuration;
        slideProgress = Mathf.Clamp01(slideProgress);

        float smoothStep = Mathf.SmoothStep(0f, 1f, slideProgress);

        // Move main object
        if (isSlidingRight)
            transform.position = Vector3.Lerp(originalPosition, targetPosition, smoothStep);
        else
            transform.position = Vector3.Lerp(targetPosition, originalPosition, smoothStep);

        // Move all secondary objects
        for (int i = 0; i < secondaryObjects.Count; i++)
        {
            if (secondaryObjects[i] == null) continue;

            if (isSlidingRight)
                secondaryObjects[i].transform.position = Vector3.Lerp(secondaryOriginalPositions[i], secondaryTargetPositions[i], smoothStep);
            else
                secondaryObjects[i].transform.position = Vector3.Lerp(secondaryTargetPositions[i], secondaryOriginalPositions[i], smoothStep);
        }

        if (slideProgress >= 1f)
        {
            isSliding = false;
        }
    }
}
