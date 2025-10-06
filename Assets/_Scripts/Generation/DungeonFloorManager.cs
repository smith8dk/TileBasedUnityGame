using System;
using UnityEngine;

public class DungeonFloorManager : MonoBehaviour
{
    public static DungeonFloorManager Instance { get; private set; }

    [Tooltip("Optional: assign the dungeon generator instance here. If null, will auto-find one at Start.")]
    [SerializeField] private CorridorFirstDungeonGenerator dungeonGenerator;

    [Header("Milestone Settings")]
    [Tooltip("Fire the OnMilestoneReached event whenever the CurrentFloor is an exact multiple of this value.")]
    [SerializeField, Min(1)] private int milestoneInterval = 10;

    /// <summary>1-based floor number. Starts at 0 until the first dungeon generation completes.</summary>
    public int CurrentFloor { get; private set; } = 0;

    /// <summary>Fired after the floor counter is updated. Parameter = new floor number.</summary>
    public event Action<int> OnFloorChanged;

    /// <summary>Fired whenever the player reaches a floor that is a positive multiple of <see cref="milestoneInterval"/>. Parameter = the floor number reached.</summary>
    public event Action<int> OnMilestoneReached;

    /// <summary>Public accessor for the milestone interval (read-only at runtime).</summary>
    public int MilestoneInterval => milestoneInterval;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
        // Optionally persist between scenes:
        // DontDestroyOnLoad(gameObject);
        CurrentFloor = 1;
    }

    private void Start()
    {
        if (dungeonGenerator == null)
            dungeonGenerator = FindObjectOfType<CorridorFirstDungeonGenerator>();

        SubscribeToGenerator();
    }

    private void OnDestroy()
    {
        UnsubscribeFromGenerator();
        if (Instance == this) Instance = null;
    }

    private void SubscribeToGenerator()
    {
        if (dungeonGenerator != null)
        {
            // avoid double subscription
            dungeonGenerator.OnDungeonGenerationCompleted -= HandleDungeonGenerated;
            dungeonGenerator.OnDungeonGenerationCompleted += HandleDungeonGenerated;
        }
        else
        {
            Debug.LogWarning("[DungeonFloorManager] No CorridorFirstDungeonGenerator found or assigned.");
        }
    }

    private void UnsubscribeFromGenerator()
    {
        if (dungeonGenerator != null)
            dungeonGenerator.OnDungeonGenerationCompleted -= HandleDungeonGenerated;
    }

    private void HandleDungeonGenerated()
    {
        // increment floor counter each time CorridorFirstGeneration finishes
        CurrentFloor++;
        Debug.Log($"[DungeonFloorManager] Dungeon generation completed. Now on floor #{CurrentFloor}");
        OnFloorChanged?.Invoke(CurrentFloor);

        // Check and fire milestone if appropriate
        CheckAndFireMilestoneEvent();
    }

    /// <summary>
    /// Reset floor counter to the provided value. Will trigger OnFloorChanged and also the milestone event if the new value matches.
    /// </summary>
    public void ResetFloor(int newFloor = 0)
    {
        CurrentFloor = newFloor;
        Debug.Log($"[DungeonFloorManager] Floor reset to {CurrentFloor}");
        OnFloorChanged?.Invoke(CurrentFloor);

        // Check and fire milestone if appropriate
        CheckAndFireMilestoneEvent();
    }

    /// <summary>
    /// Manually increment floor (useful if you want to override generator event).
    /// Fires OnFloorChanged and the milestone event as appropriate.
    /// </summary>
    public void IncrementFloor()
    {
        CurrentFloor++;
        Debug.Log($"[DungeonFloorManager] Floor manually incremented to {CurrentFloor}");
        OnFloorChanged?.Invoke(CurrentFloor);

        // Check and fire milestone if appropriate
        CheckAndFireMilestoneEvent();
    }

    /// <summary>
    /// If you later swap or spawn a different generator instance at runtime, call this to rebind the event.
    /// </summary>
    public void BindToGenerator(CorridorFirstDungeonGenerator newGenerator)
    {
        UnsubscribeFromGenerator();
        dungeonGenerator = newGenerator;
        SubscribeToGenerator();
    }

    /// <summary>
    /// Centralized check that fires OnMilestoneReached when CurrentFloor is a positive multiple of milestoneInterval.
    /// </summary>
    private void CheckAndFireMilestoneEvent()
    {
        // Defensive: ensure milestoneInterval is at least 1 (Min attribute prevents this through Inspector)
        if (milestoneInterval <= 0) milestoneInterval = 1;

        if (CurrentFloor > 0 && CurrentFloor % milestoneInterval == 0)
        {
            Debug.Log($"[DungeonFloorManager] Milestone reached: floor {CurrentFloor} (every {milestoneInterval} floors).");
            OnMilestoneReached?.Invoke(CurrentFloor);
        }
    }
}
