using UnityEngine;

/// <summary>
/// Scene-level singleton that provides a small API used by SpellManager / EventClick to
/// start/cancel per-draggable turn-based cooldowns and to tick them each enemy turn.
/// 
/// Important:
/// - This looks for a DraggableCooldown component on the provided GameObject (or its children).
/// - Each draggable must have its own DraggableCooldown component for independent cooldowns.
/// - Attach this script to a persistent GameObject in your scene (e.g. "GameManager") or create one.
/// </summary>
public class CooldownManager : MonoBehaviour
{
    private static CooldownManager _instance;
    public static CooldownManager Instance
    {
        get
        {
            if (_instance == null)
            {
                // try to find an existing one in scene
                _instance = FindObjectOfType<CooldownManager>();
            }
            return _instance;
        }
    }

    private void Awake()
    {
        if (_instance == null)
        {
            _instance = this;
            // optional: DontDestroyOnLoad(gameObject);
        }
        else if (_instance != this)
        {
            Debug.LogWarning("[CooldownManager] Duplicate instance detected — destroying extra.");
            Destroy(gameObject);
        }
    }

    /// <summary>
    /// Start a cooldown on the specified GameObject (or its children). If no DraggableCooldown
    /// component is found, logs a warning.
    /// </summary>
    public void StartCooldownOn(GameObject sourceDraggable, int turns)
    {
        if (sourceDraggable == null)
        {
            Debug.LogWarning("[CooldownManager] StartCooldownOn called with null sourceDraggable.");
            return;
        }

        var cd = sourceDraggable.GetComponent<DraggableCooldown>() ?? sourceDraggable.GetComponentInChildren<DraggableCooldown>();
        if (cd == null)
        {
            Debug.LogWarningFormat("[CooldownManager] No DraggableCooldown found on '{0}' (or children). Cannot start cooldown of {1} turns.",
                sourceDraggable.name, turns);
            return;
        }

        try
        {
            cd.StartCooldown(turns);
            Debug.LogFormat("[CooldownManager] Started cooldown of {0} turns on '{1}'.", turns, sourceDraggable.name);
        }
        catch (System.Exception ex)
        {
            Debug.LogException(ex);
        }
    }

    /// <summary>
    /// Convenience: find a GameObject by name and start the cooldown on it.
    /// (Use carefully — GameObject.Find is not recommended for heavy usage.)
    /// </summary>
    public void StartCooldownByName(string draggableName, int turns)
    {
        if (string.IsNullOrEmpty(draggableName))
        {
            Debug.LogWarning("[CooldownManager] StartCooldownByName called with null/empty name.");
            return;
        }

        var go = GameObject.Find(draggableName);
        if (go == null)
        {
            Debug.LogWarningFormat("[CooldownManager] StartCooldownByName: no GameObject named '{0}' found.", draggableName);
            return;
        }

        StartCooldownOn(go, turns);
    }

    /// <summary>
    /// Cancel an active cooldown on the given draggable if it has a DraggableCooldown component.
    /// </summary>
    public void CancelCooldownOn(GameObject sourceDraggable)
    {
        if (sourceDraggable == null) return;
        var cd = sourceDraggable.GetComponent<DraggableCooldown>() ?? sourceDraggable.GetComponentInChildren<DraggableCooldown>();
        if (cd != null)
        {
            try
            {
                cd.CancelCooldown();
                Debug.LogFormat("[CooldownManager] Cancelled cooldown on '{0}'.", sourceDraggable.name);
            }
            catch (System.Exception ex)
            {
                Debug.LogException(ex);
            }
        }
    }

    /// <summary>
    /// Forwarder called by the turn system to decrement every registered cooldown.
    /// This simply calls DraggableCooldown.TickAllTurns() directly (no reflection).
    /// </summary>
    public void TickAllTurns()
    {
        try
        {
            DraggableCooldown.TickAllTurns();
        }
        catch (System.Exception ex)
        {
            Debug.LogException(ex);
        }
    }
}
