using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Per-draggable turn-based cooldown helper.
/// - Attach to the GameObject (or child) that represents a draggable spell slot.
/// - Call StartCooldown(int) to start a cooldown on that single instance.
/// - Call DraggableCooldown.TickAllTurns() once per enemy turn to decrement every cooldown.
/// - Query IsOnCooldown or GetRemainingTurns() to block clicks or show UI.
/// 
/// This version avoids any reflection loop and protects against re-entrant ticks.
/// </summary>
public class DraggableCooldown : MonoBehaviour
{
    [Tooltip("Default number of turns of cooldown when started via StartCooldown(defaultCooldownTurns)")]
    public int defaultCooldownTurns = 3;

    // Instance state: per-draggable remaining turn counter (NOT static)
    [SerializeField, Tooltip("Remaining turns of cooldown (runtime)")]
    private int remainingTurns = 0;

    // Static registry of all instances so TickAllTurns can find and tick them.
    private static readonly HashSet<DraggableCooldown> allCooldowns = new HashSet<DraggableCooldown>();

    // Events that external code can subscribe to.
    // These are static so any system can listen for cooldown start/end across all draggables.
    public static event Action<DraggableCooldown> OnCooldownStarted;
    public static event Action<DraggableCooldown> OnCooldownEnded;

    // Reentrancy guard for static ticking
    private static bool s_isTicking = false;

    // Read-only accessor
    public bool IsOnCooldown => remainingTurns > 0;

    private void Awake()
    {
        RegisterSelf();
    }

    private void OnEnable()
    {
        RegisterSelf();
    }

    private void OnDisable()
    {
        UnregisterSelf();
    }

    private void OnDestroy()
    {
        UnregisterSelf();
    }

    private void RegisterSelf()
    {
        lock (allCooldowns)
        {
            allCooldowns.Add(this);
        }
    }

    private void UnregisterSelf()
    {
        lock (allCooldowns)
        {
            allCooldowns.Remove(this);
        }
    }

    /// <summary>
    /// Start a cooldown on this draggable instance.
    /// If turns <= 0 nothing happens.
    /// </summary>
    public void StartCooldown(int turns)
    {
        if (turns <= 0) return;

        // set remaining turns to the requested duration
        remainingTurns = turns;

        // ensure we're registered
        lock (allCooldowns) { allCooldowns.Add(this); }

        Debug.LogFormat("[DraggableCooldown] Started cooldown on '{0}' for {1} turns.", name, remainingTurns);
        try { OnCooldownStarted?.Invoke(this); } catch (Exception ex) { Debug.LogException(ex); }
    }

    /// <summary>
    /// Immediately cancel/clear cooldown on this instance.
    /// </summary>
    public void CancelCooldown()
    {
        if (remainingTurns <= 0) return;
        remainingTurns = 0;
        Debug.LogFormat("[DraggableCooldown] Cancelled cooldown on '{0}'.", name);
        try { OnCooldownEnded?.Invoke(this); } catch (Exception ex) { Debug.LogException(ex); }
    }

    /// <summary>
    /// Called by the turn system once per enemy turn (or however you count turns) for this instance.
    /// </summary>
    public void TickTurn()
    {
        if (remainingTurns <= 0) return;

        remainingTurns = Mathf.Max(0, remainingTurns - 1);
        Debug.LogFormat("[DraggableCooldown] TickTurn '{0}' -> {1} turns remaining.", name, remainingTurns);

        if (remainingTurns == 0)
        {
            try { OnCooldownEnded?.Invoke(this); } catch (Exception ex) { Debug.LogException(ex); }
            // Note: we intentionally keep the instance in the registry so future StartCooldown calls still work seamlessly.
        }
    }

    /// <summary>
    /// Static helper: tick all registered cooldown instances once.
    /// Call this from your PlayerController.OnEnemyTurnComplete() (or equivalent) once per enemy turn.
    /// This method is protected against re-entrant calls which previously caused stack overflows.
    /// </summary>
    public static void TickAllTurns()
    {
        // Prevent recursion / re-entrancy that caused the stack overflow
        if (s_isTicking)
        {
            Debug.LogWarning("[DraggableCooldown] TickAllTurns called re-entrantly — ignoring nested call.");
            return;
        }

        try
        {
            s_isTicking = true;

            // Make a snapshot to avoid issues if Start/Cancel occurs while iterating
            DraggableCooldown[] snapshot;
            lock (allCooldowns)
            {
                snapshot = new DraggableCooldown[allCooldowns.Count];
                allCooldowns.CopyTo(snapshot);
            }

            foreach (var cd in snapshot)
            {
                if (cd == null) continue;
                try { cd.TickTurn(); } catch (Exception ex) { Debug.LogException(ex); }
            }
        }
        finally
        {
            s_isTicking = false;
        }
    }

    /// <summary>
    /// Read how many turns remain on this instance (0 = not on cooldown).
    /// </summary>
    public int GetRemainingTurns() => remainingTurns;

#if UNITY_EDITOR
    [UnityEditor.MenuItem("CONTEXT/DraggableCooldown/Debug_PrintAllCooldowns")]
    private static void Debug_PrintAllCooldowns() {
        string s = "DraggableCooldown registry snapshot:\n";
        lock (allCooldowns) {
            foreach (var c in allCooldowns) {
                if (c == null) continue;
                s += $"{c.name}: {c.remainingTurns} turns\n";
            }
        }
        Debug.Log(s);
    }
#endif
}
