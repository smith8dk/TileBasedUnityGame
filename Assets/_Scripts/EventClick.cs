using System;
using System.Reflection;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Events; // Required for UnityEvent

/// <summary>
/// Robust EventClick that works with multiple versions of DraggableCooldown by using reflection.
/// Also passes source draggable into SpellManager.TakeAim so cooldowns can be applied on spawn.
/// </summary>
public class EventClick : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] public GameObject[] spellPrefabs; // Array of spell prefabs
    [SerializeField] public GameObject[] hitboxPrefabs; // Array of hitbox prefabs
    private GameObject spellInstance; // Instance of the spell object
    private GameObject hitboxInstance; // Instance of the hitbox object
    private SpellManager spellManager; // Reference to the SpellManager

    // Reference to the UIHandler
    [SerializeField] private UIHandler uiHandler;

    // Custom UnityEvent for when the object is clicked
    public UnityEvent onObjectClicked;

    private void Start()
    {
        // Find and cache the SpellManager
        spellManager = FindObjectOfType<SpellManager>();
        if (spellManager == null)
        {
            Debug.LogError("[EventClick] SpellManager not found in the scene.");
        }

        // Ensure the event is initialized
        if (onObjectClicked == null)
        {
            onObjectClicked = new UnityEvent();
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        try
        {
            // Find the DraggableCooldown component (if any) near this clickable
            var cd = FindCooldownComponent();

            // Defensive: if there's a turn-based cooldown component on this draggable, block if active
            if (cd != null)
            {
                int remaining = GetCooldownRemainingTurns(cd);
                bool onCooldown = IsCooldownActive(cd, remaining);

                if (onCooldown)
                {
                    // IMPORTANT: explicitly mark the event consumed so dispatcher/owner won't double-handle it.
                    // This avoids follow-up owner/RMF processing that previously caused the cooldown to "freeze".
                    if (eventData != null)
                        eventData.Use();

                    Debug.LogFormat("[EventClick] Click blocked: '{0}' is on cooldown ({1} turns remaining).", gameObject.name, (remaining >= 0 ? remaining.ToString() : "unknown"));
                    return;
                }
            }

            // Log eventData info (safe checks)
            if (eventData == null)
            {
                Debug.LogWarning("[EventClick] PointerEventData is null.");
            }
            else
            {
                GameObject raycastGo = eventData.pointerCurrentRaycast.gameObject;
                Debug.LogFormat("[EventClick] Pointer raycast target: {0}", (raycastGo != null) ? raycastGo.name : "null");
            }

            // Check parent context
            if (transform.parent != null)
            {
                Debug.LogFormat("[EventClick] Parent: '{0}'", transform.parent.name);
                if (transform.parent.name == "Side-Menu")
                {
                    Debug.Log("[EventClick] No action performed. Object is a child of Side-Menu.");
                    return;
                }
            }
            else
            {
                Debug.Log("[EventClick] GameObject has no parent.");
            }

            // Check child count before calling GetChild(0)
            int childCount = gameObject.transform.childCount;

            if (childCount == 0)
            {
                Debug.LogErrorFormat("[EventClick] ERROR: GameObject '{0}' has no children but code attempted to access child index 0. Aborting click handling.", gameObject.name);
                return;
            }

            // Safely get child 0 and log its hierarchy path
            Transform child = gameObject.transform.GetChild(0);
            string childPath = GetHierarchyPath(child);

            // Validate prefab arrays
            int spellsLen = (spellPrefabs != null) ? spellPrefabs.Length : 0;
            int hitboxesLen = (hitboxPrefabs != null) ? hitboxPrefabs.Length : 0;

            if (spellsLen == 0)
            {
                Debug.LogWarning("[EventClick] Warning: spellPrefabs is empty or null.");
            }
            if (hitboxesLen == 0)
            {
                Debug.LogWarning("[EventClick] Warning: hitboxPrefabs is empty or null.");
            }
            if (hitboxesLen != spellsLen)
            {
                Debug.LogWarning("[EventClick] Warning: spellPrefabs and hitboxPrefabs lengths differ. This may cause index issues.");
            }

            // Loop and compare names with detailed debug info
            for (int i = 0; i < spellsLen; i++)
            {
                if (spellPrefabs[i] == null) continue;

                if (spellPrefabs[i].name == child.name)
                {
                    // If the object is found in the array, log the index
                    spellInstance = spellPrefabs[i];

                    // attempt to get hitbox instance if available
                    if (i < hitboxesLen)
                    {
                        hitboxInstance = (hitboxPrefabs[i] != null) ? hitboxPrefabs[i] : null;
                        if (hitboxInstance == null)
                            Debug.LogWarningFormat("[EventClick] hitboxPrefabs[{0}] is null.", i);
                    }
                    else
                    {
                        hitboxInstance = null;
                        Debug.LogWarningFormat("[EventClick] No hitboxPrefabs[{0}] (index out of range).", i);
                    }

                    Debug.LogFormat("[EventClick] Match found at index {0}. Spell='{1}', Hitbox='{2}'", i,
                        (spellInstance != null) ? spellInstance.name : "null",
                        (hitboxInstance != null) ? hitboxInstance.name : "null");

                    // ----- before calling TakeAim, determine if this object sits in an enhanced slot -----
                    const float ENHANCED_DAMAGE_MULTIPLIER = 1.5f; // change this value to tune enhanced damage
                    float chosenMultiplier = 1f;

                    // find the RMF in the scene (fast enough for UI clicks). You can cache it if you prefer.
                    var rmf = FindObjectOfType<RMF_RadialMenu>();
                    if (rmf != null)
                    {
                        try
                        {
                            // try to call a likely method, but be defensive in case your RMF API is different
                            var mi = rmf.GetType().GetMethod("IsChildInEnhancedSlot", new Type[] { typeof(Transform) });
                            if (mi != null)
                            {
                                var isEnhanced = (bool)mi.Invoke(rmf, new object[] { this.transform });
                                if (isEnhanced) chosenMultiplier = ENHANCED_DAMAGE_MULTIPLIER;
                            }
                        }
                        catch { /* ignore if absent */ }
                    }

                    if (spellManager != null)
                    {
                        // Determine source draggable GameObject to pass to SpellManager so cooldown can be attached to it on spawn.
                        GameObject sourceDraggable = FindClosestDraggableGameObject();

                        // call the updated TakeAim signature (spellPrefab, hitboxPrefab, multiplier, sourceDraggable)
                        // SpellManager.TakeAim is expected to accept the sourceDraggable (your SpellManager was updated earlier).
                        try
                        {
                            spellManager.TakeAim(spellInstance, hitboxInstance, chosenMultiplier, sourceDraggable);
                        }
                        catch (MissingMethodException)
                        {
                            // fallback for versions that don't accept sourceDraggable
                            spellManager.TakeAim(spellInstance, hitboxInstance, chosenMultiplier);
                        }
                        onObjectClicked.Invoke();

                        // Immediately start cooldown if you prefer to apply cooldown at click-time (optional).
                        // We will attempt to start cooldown using reflection so it works across versions.
                        // Only start cooldown here if a cooldown component exists and the slot is not already on cooldown.
                        if (cd != null)
                        {
                            int remainingNow = GetCooldownRemainingTurns(cd);
                            if (!IsCooldownActive(cd, remainingNow))
                                TryStartCooldownOnComponent(cd);
                        }
                    }
                    else
                    {
                        Debug.LogError("[EventClick] SpellManager reference is null. Cannot call TakeAim.");
                    }
                    return;
                }
            }

            Debug.LogWarningFormat("[EventClick] No matching spell prefab found for child '{0}' (path='{1}').", child.name, childPath);
        }
        catch (System.Exception ex)
        {
            Debug.LogErrorFormat("[EventClick] EXCEPTION in OnPointerClick: {0}\n{1}", ex.Message, ex.StackTrace);
        }
    }

    // Helper: returns a full hierarchy path for a transform (Parent/.../Child)
    private string GetHierarchyPath(Transform t)
    {
        if (t == null) return "null";
        string path = t.name;
        Transform cur = t.parent;
        while (cur != null)
        {
            path = cur.name + "/" + path;
            cur = cur.parent;
        }
        return path;
    }

    // -------------------------
    // Reflection-based cooldown helpers
    // -------------------------

    // Attempts to locate DraggableCooldown on this object (self, children, or parent)
    private DraggableCooldown FindCooldownComponent()
    {
        var cd = GetComponent<DraggableCooldown>();
        if (cd != null) return cd;
        cd = GetComponentInChildren<DraggableCooldown>();
        if (cd != null) return cd;
        cd = GetComponentInParent<DraggableCooldown>();
        return cd;
    }

    // Tries multiple common names to read remaining turns. Returns -1 if unknown/unavailable.
    private int GetCooldownRemainingTurns(DraggableCooldown cd)
    {
        if (cd == null) return -1;

        Type t = cd.GetType();

        // 1) Method GetRemainingTurns()
        var mi = t.GetMethod("GetRemainingTurns", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (mi != null && mi.ReturnType == typeof(int))
        {
            try { return (int)mi.Invoke(cd, null); } catch { }
        }

        // 2) Property names: CooldownRemaining, RemainingTurns, cooldownRemaining
        string[] propNames = new string[] { "CooldownRemaining", "RemainingTurns", "Remaining", "cooldownRemaining", "remainingTurns" };
        foreach (var pn in propNames)
        {
            var pi = t.GetProperty(pn, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (pi != null && (pi.PropertyType == typeof(int) || pi.PropertyType == typeof(float)))
            {
                try
                {
                    var val = pi.GetValue(cd);
                    if (val is int iv) return iv;
                    if (val is float fv) return Mathf.RoundToInt(fv);
                }
                catch { }
            }

            var fi = t.GetField(pn, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (fi != null && (fi.FieldType == typeof(int) || fi.FieldType == typeof(float)))
            {
                try
                {
                    var val = fi.GetValue(cd);
                    if (val is int iv) return iv;
                    if (val is float fv) return Mathf.RoundToInt(fv);
                }
                catch { }
            }
        }

        // 3) Method GetCooldownRemaining() or GetRemaining()
        string[] methodNames = new string[] { "GetCooldownRemaining", "GetRemaining", "RemainingTurns" };
        foreach (var mn in methodNames)
        {
            mi = t.GetMethod(mn, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (mi != null && mi.ReturnType == typeof(int))
            {
                try { return (int)mi.Invoke(cd, null); } catch { }
            }
        }

        // unknown
        return -1;
    }

    // Tries to determine boolean cooldown state; returns false if unknown but remaining==0
    private bool IsCooldownActive(DraggableCooldown cd, int remainingFromProbe)
    {
        if (cd == null) return false;

        Type t = cd.GetType();

        // 1) bool property IsOnCooldown / isOnCooldown
        string[] boolProps = new string[] { "IsOnCooldown", "isOnCooldown", "OnCooldown", "onCooldown" };
        foreach (var pn in boolProps)
        {
            var pi = t.GetProperty(pn, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (pi != null && pi.PropertyType == typeof(bool))
            {
                try { return (bool)pi.GetValue(cd); } catch { }
            }

            var fi = t.GetField(pn, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (fi != null && fi.FieldType == typeof(bool))
            {
                try { return (bool)fi.GetValue(cd); } catch { }
            }
        }

        // 2) method IsOnCooldown()
        var mi = t.GetMethod("IsOnCooldown", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (mi != null && mi.ReturnType == typeof(bool))
        {
            try { return (bool)mi.Invoke(cd, null); } catch { }
        }

        // 3) fallback to integer remaining if available
        if (remainingFromProbe >= 1) return true;
        if (remainingFromProbe == 0) return false;

        // unknown: assume false (not blocked) to avoid preventing the user
        return false;
    }

    // Attempts to start a cooldown on this DraggableCooldown using reflection
    private void TryStartCooldownOnComponent(DraggableCooldown cd)
    {
        if (cd == null) return;

        Type t = cd.GetType();

        // 1) Try method StartCooldown(int turns)
        var mi = t.GetMethod("StartCooldown", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new Type[] { typeof(int) }, null);
        if (mi != null)
        {
            int turns = GetDefaultCooldownTurns(cd);
            try
            {
                mi.Invoke(cd, new object[] { turns });
                Debug.LogFormat("[EventClick] Started cooldown of {0} turns (via StartCooldown) on '{1}'", turns, cd.name);
                return;
            }
            catch (Exception ex)
            {
                Debug.LogWarningFormat("[EventClick] StartCooldown invoke failed: {0}", ex.Message);
            }
        }

        // 2) Try StartCooldown() without args
        mi = t.GetMethod("StartCooldown", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
        if (mi != null)
        {
            try
            {
                mi.Invoke(cd, null);
                Debug.LogFormat("[EventClick] Started cooldown (no-arg StartCooldown) on '{0}'", cd.name);
                return;
            }
            catch (Exception ex)
            {
                Debug.LogWarningFormat("[EventClick] StartCooldown() invoke failed: {0}", ex.Message);
            }
        }

        // 3) If no StartCooldown method, try to set a remaining field/property directly (last resort)
        int defaultTurns = GetDefaultCooldownTurns(cd);
        string[] possibleNames = new string[] { "CooldownRemaining", "cooldownRemaining", "RemainingTurns", "remainingTurns" };
        foreach (var name in possibleNames)
        {
            var pi = t.GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (pi != null && (pi.PropertyType == typeof(int) || pi.PropertyType == typeof(float)))
            {
                try
                {
                    if (pi.PropertyType == typeof(int)) pi.SetValue(cd, defaultTurns);
                    else pi.SetValue(cd, (float)defaultTurns);
                    Debug.LogFormat("[EventClick] Set {0}={1} on '{2}' (reflection fallback)", name, defaultTurns, cd.name);
                    return;
                }
                catch { }
            }

            var fi = t.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (fi != null && (fi.FieldType == typeof(int) || fi.FieldType == typeof(float)))
            {
                try
                {
                    if (fi.FieldType == typeof(int)) fi.SetValue(cd, defaultTurns);
                    else fi.SetValue(cd, (float)defaultTurns);
                    Debug.LogFormat("[EventClick] Set {0}={1} on '{2}' (reflection fallback)", name, defaultTurns, cd.name);
                    return;
                }
                catch { }
            }
        }


        Debug.LogWarningFormat("[EventClick] Couldn't start cooldown on '{0}' — no known API found. Consider adding StartCooldown(int) or a CooldownRemaining property to DraggableCooldown.", cd.name);
    }

    // Attempts to find a defaultCooldownTurns value on the component (field/property). Falls back to 1.
    private int GetDefaultCooldownTurns(DraggableCooldown cd)
    {
        if (cd == null) return 1;

        Type t = cd.GetType();

        // Try common names
        string[] names = new string[] { "defaultCooldownTurns", "DefaultCooldownTurns", "defaultCooldown", "cooldownDefault" };

        foreach (var n in names)
        {
            var fi = t.GetField(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (fi != null && fi.FieldType == typeof(int))
            {
                try { return (int)fi.GetValue(cd); } catch { }
            }

            var pi = t.GetProperty(n, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (pi != null && pi.PropertyType == typeof(int))
            {
                try { return (int)pi.GetValue(cd); } catch { }
            }
        }

        // If the component exposes a method to get default, try that
        var mi = t.GetMethod("GetDefaultCooldownTurns", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        if (mi != null && mi.ReturnType == typeof(int))
        {
            try { return (int)mi.Invoke(cd, null); } catch { }
        }

        return 1;
    }

    // Finds the closest GameObject that is likely the draggable container to pass as sourceDraggable to SpellManager.
    // Common cases: this.gameObject, parent's GameObject, or child that has Draggable component.
    private GameObject FindClosestDraggableGameObject()
    {
        // If this object has a Draggable, prefer that GameObject
        var dr = GetComponent<Draggable>() ?? GetComponentInChildren<Draggable>() ?? GetComponentInParent<Draggable>();
        if (dr != null) return dr.gameObject;

        // Otherwise, try the GameObject that owns this EventClick (likely the clickable slot)
        return this.gameObject;
    }
}
