using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// SpearFallSpell:
/// - is a stationary Spell target that waits `initialWaitTurns` MoveFurther() calls then spawns a spear prefab
///   (from top-left camera) toward itself.
/// - When it spawns, it hides its own visuals and notifies its group manager.
/// - Arrival detection is used only to mark that the spear has arrived; damage is handled by the spear prefab's own script.
/// - Groups: when all members of a group have spawned, the group is destroyed on the next MoveFurther turn:
///   all member targets and any spawned spear objects created by that group are removed together.
/// </summary>
public class SpearFallSpell : Spell
{
    [Header("Spear spawn")]
    [SerializeField] private GameObject spearPrefab;
    [SerializeField, Range(0f, 1f)] private float spawnViewportX = 0.05f;
    [SerializeField, Range(0f, 1f)] private float spawnViewportY = 0.95f;

    [Header("Turn delay")]
    [SerializeField] private int initialWaitTurns = 1;

    // runtime state
    private int waitTurns = 0;
    private bool hasSpawned = false;
    private GameObject spawnedSpear = null;

    // arrival trigger helper (child GameObject + component

    // group management
    private int groupId = 0;
    private static int s_nextGroupId = 1;

    private class GroupData
    {
        public int id;
        public HashSet<SpearFallSpell> members = new HashSet<SpearFallSpell>();
        public List<GameObject> spawnedSpears = new List<GameObject>();
        public bool destroyScheduled = false;
        public int destroyFrame = -1;
    }

    private static readonly Dictionary<int, GroupData> s_groups = new Dictionary<int, GroupData>();

    /// <summary>
    /// Reserve a new group id for a SpearStorm cast. Call before instantiating targets.
    /// </summary>
    public static int CreateNewGroup()
    {
        int id = s_nextGroupId++;
        var g = new GroupData { id = id };
        s_groups[id] = g;
        return id;
    }

    /// <summary>
    /// Assign this target to a group id. Call BEFORE Initialize so registration happens in Initialize.
    /// </summary>
    public void AssignToGroup(int id) => groupId = id;

    public void SetInitialWaitTurns(int turns)
    {
        initialWaitTurns = Mathf.Max(0, turns);
        waitTurns = initialWaitTurns;
    }

    protected override void Start()
    {
        base.Start();

        // remove movePoint - this target is stationary and doesn't need it
        if (movePoint != null)
        {
            Destroy(movePoint.gameObject);
            movePoint = null;
        }

        isMoving = false;
        baseSpeed = 0f;
        currentSpeed = 0f;

        waitTurns = Mathf.Max(0, initialWaitTurns);
    }

    public override void Initialize(Vector3 dir, GameObject owner = null, GameObject sourceDraggable = null, int cooldownTurns = 0)
    {
        // owner handling (so collisions ignore owner if needed)
        this.owner = owner;
        ApplyOwnerCollisionIgnore();

        // stationary setup
        direction = Vector2.zero;
        startPosition = transform.position;
        currentSegmentLength = 0f;
        targetPosition = startPosition;
        if (movePoint != null) movePoint.position = transform.position;

        isMoving = false;
        moveTime = 0f;
        baseSpeed = 0f;
        currentSpeed = 0f;

        waitTurns = Mathf.Max(0, initialWaitTurns);
        hasSpawned = false;

        // register to a group if assigned
        RegisterToGroup();
    }

    private void RegisterToGroup()
    {
        if (groupId == 0) return;
        if (!s_groups.TryGetValue(groupId, out var g))
        {
            g = new GroupData { id = groupId };
            s_groups[groupId] = g;
        }
        g.members.Add(this);
    }

    private void UnregisterFromGroup()
    {
        if (groupId == 0) return;
        if (s_groups.TryGetValue(groupId, out var g))
        {
            g.members.Remove(this);
            if (g.members.Count == 0 && !g.destroyScheduled)
                s_groups.Remove(groupId);
        }
        groupId = 0;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        UnregisterFromGroup();
    }

    /// <summary>
    /// Turn-step called by Spell system. Waits then spawns spear.
    /// </summary>
    protected override void MoveFurther()
    {
        // stay immobile
        isMoving = false;

        // if already spawned, possibly perform scheduled group destruction
        if (hasSpawned)
        {
            TryPerformGroupDestructionIfNeeded();
            return;
        }

        // decrement and wait
        if (waitTurns > 0) waitTurns--;
        if (waitTurns > 0) return;

        // spawn spear
        SpawnSpear();
    }

    private void SpawnSpear()
    {
        if (hasSpawned) return;
        hasSpawned = true;

        Camera cam = Camera.main;
        if (cam == null || spearPrefab == null) return;

        float camZ = cam.transform.position.z;
        Vector3 spawnViewport = new Vector3(spawnViewportX, spawnViewportY, Mathf.Abs(camZ));
        Vector3 spawnWorld = cam.ViewportToWorldPoint(spawnViewport);
        spawnWorld.z = 0f;

        Vector3 targetWorld = transform.position;
        targetWorld.z = 0f;

        // instantiate spear
        GameObject spearGO = Instantiate(spearPrefab, spawnWorld, Quaternion.identity);
        spawnedSpear = spearGO;

        // If the spear itself is a Spell, initialize it (so it will move according to Spell behavior).
        // Otherwise, perform a short tween to the target so it visually moves into place.
        var spearSpell = spearGO.GetComponent<Spell>();
        if (spearSpell != null)
        {
            Vector3 dir = (targetWorld - spawnWorld);
            if (dir.sqrMagnitude < 1e-6f) dir = Vector3.down;
            spearSpell.Initialize(dir, this.owner);
            RegisterSpawnedSpearToGroup(spearGO);
        }
        else
        {
            StartCoroutine(MoveNonSpellToTarget(spearGO, targetWorld, 0.2f));
            RegisterSpawnedSpearToGroup(spearGO);
        }

        // hide this target's visuals (keeps object alive for group bookkeeping)
        HideVisualsButKeepAlive();

        // schedule group destruction if all members have spawned
        ScheduleGroupDestroyIfComplete();
    }

    private IEnumerator MoveNonSpellToTarget(GameObject go, Vector3 target, float duration)
    {
        if (go == null) yield break;
        Vector3 start = go.transform.position;
        float t = 0f;
        while (t < duration)
        {
            if (go == null) yield break;
            t += Time.deltaTime;
            go.transform.position = Vector3.Lerp(start, target, Mathf.Clamp01(t / duration));
            yield return null;
        }
    }


    /// <summary>
    /// Called when a spear reaches the arrival trigger. We only mark that the spear arrived,
    /// disable the arrival trigger and hide visuals. Damage should be handled inside the spear prefab (SpearProjectile).
    /// </summary>
    internal void OnSpearArrived(GameObject spear)
    {
        if (!hasSpawned) hasSpawned = true;
        if (spawnedSpear == null) spawnedSpear = spear;

        // Note: do NOT perform damage here — the spear prefab (SpearProjectile) should handle damage.
        // Keep this target hidden (it was already hidden on spawn), and let group lifecycle handle cleanup.

        // schedule destruction check (if this was the last member)
        ScheduleGroupDestroyIfComplete();
    }

    private void HideVisualsButKeepAlive()
    {
        foreach (var r in GetComponentsInChildren<Renderer>(true))
            if (r != null) r.enabled = false;
    }

    private void RegisterSpawnedSpearToGroup(GameObject spear)
    {
        if (groupId == 0 || spear == null) return;
        if (!s_groups.TryGetValue(groupId, out var g)) return;
        g.spawnedSpears.Add(spear);
    }

    private void ScheduleGroupDestroyIfComplete()
    {
        if (groupId == 0 || !s_groups.TryGetValue(groupId, out var g)) return;

        int spawnedMembers = 0;
        foreach (var m in g.members)
            if (m != null && m.hasSpawned) spawnedMembers++;

        if (spawnedMembers >= g.members.Count)
        {
            g.destroyScheduled = true;
            g.destroyFrame = Time.frameCount + 1; // destroy next frame/turn
        }
    }

    private void TryPerformGroupDestructionIfNeeded()
    {
        if (groupId == 0 || !s_groups.TryGetValue(groupId, out var g)) return;
        if (!g.destroyScheduled) return;
        if (Time.frameCount >= g.destroyFrame) DestroyGroup(groupId);
    }

    private void DestroyGroup(int gid)
    {
        if (!s_groups.TryGetValue(gid, out var g)) return;

        // destroy all spawned spears first (if any)
        foreach (var sgo in g.spawnedSpears)
        {
            if (sgo != null)
            {
                try { Destroy(sgo); } catch { }
            }
        }
        g.spawnedSpears.Clear();

        // destroy all member target objects
        var membersSnapshot = new List<SpearFallSpell>(g.members);
        foreach (var member in membersSnapshot)
        {
            if (member != null)
            {
                try { Destroy(member.gameObject); } catch { }
            }
        }

        s_groups.Remove(gid);
    }

    // no per-instance trigger behavior on the target itself
    protected override void OnTriggerEnter2D(Collider2D other) { }
}
