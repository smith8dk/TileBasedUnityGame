using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// EscalateSpell: when initialized it spawns one copy exactly one tile away from the owner
/// in each cardinal direction (or around its own position when owner is null).
/// Each cast forms a Group; the Group advances by one step (once per frame) when any member receives MoveFurther().
/// When a Group reaches its configured step limit, ALL instances in that Group are destroyed together.
/// </summary>
public class EscalateSpell : Spell
{
    [Header("Escalation settings")]
    [Tooltip("Prefab to instantiate for spawned copies (assign the EscalateSpell prefab). If null, this GameObject will be cloned.")]
    [SerializeField] private GameObject spellPrefab;

    [Tooltip("Maximum number of new copies to create on each MoveFurther turn (per instance).")]
    [SerializeField] private int maxSpawnPerStep = 3;

    [Tooltip("How many MoveFurther turns before ALL instances of this cast are removed.")]
    [SerializeField] private int maxGroupSteps = 2;

    [Tooltip("World distance between grid cells (tile size). Initial spawn places copies exactly `cellSize` away.")]
    [SerializeField] private float cellSize = 1f;

    [Tooltip("If true, newly spawned clones will skip the initial 4-spawn in Initialize().")]
    [SerializeField] private bool defaultSuppressInitialSpawn = false;

    // Cardinal directions (right, up, left, down)
    private static readonly Vector2[] Cardinals = new Vector2[] {
        new Vector2(1f, 0f),
        new Vector2(0f, 1f),
        new Vector2(-1f, 0f),
        new Vector2(0f, -1f)
    };

    // Instance flag to prevent newly created clones from running initial 4-spawn
    [HideInInspector] public bool suppressInitialSpawn = false;

    // ---------- Group system (per-cast) ----------
    private const int UNASSIGNED_GROUP = -1;

    private class GroupData
    {
        public int id;
        public HashSet<EscalateSpell> members = new HashSet<EscalateSpell>();
        public int stepCount = 0;
        public int lastUpdatedFrame = -1;
        public int stepLimit = 2;
    }

    private static int s_nextGroupId = 1;
    private static Dictionary<int, GroupData> s_groups = new Dictionary<int, GroupData>();

    private int groupId = UNASSIGNED_GROUP;

    // ---------- Lifecycle ----------

    public override void Initialize(Vector3 dir, GameObject owner = null, GameObject sourceDraggable = null, int cooldownTurns = 0)
    {
        // Set owner and apply collision-ignore
        this.owner = owner;
        ApplyOwnerCollisionIgnore();

        // Force immobile & no rotation
        direction = Vector2.zero;
        transform.rotation = Quaternion.identity; // <-- lock rotation here
        startPosition = transform.position;
        currentSegmentLength = 0f;
        targetPosition = startPosition;

        isMoving = false;
        moveTime = 0f;
        baseSpeed = 0f;
        currentSpeed = 0f;

        // Group setup
        if (groupId == UNASSIGNED_GROUP && !suppressInitialSpawn && !defaultSuppressInitialSpawn)
            CreateNewGroup(maxGroupSteps);

        RegisterToGroupIfNeeded();

        // Initial 4-spawn
        if (!suppressInitialSpawn && !defaultSuppressInitialSpawn)
        {
            Vector3 center = (owner != null) ? owner.transform.position : transform.position;
            SpawnAroundCenterExact(center);
        }
    }
    protected virtual void Start()
    {
        Collider2D myCol = GetComponent<Collider2D>();
        if (myCol != null)
        {
            // force re-enable so IgnoreCollision calls take effect correctly
            myCol.enabled = false;
            myCol.enabled = true;

            // Build masks
            int exitMask = (int)exitTileInteraction;
            int allowedMask = 0;
            allowedMask |= (int)whatStopsMovement; // we WANT collisions with obstacles
            // NOTE: DO NOT include exitTileInteraction in allowedMask — we want to IGNORE it
            allowedMask |= (int)enemyLayer;
            allowedMask |= (int)playerLayer; // include player so we can hit them (unless owner)

            // Iterate all colliders in scene and set IgnoreCollision appropriately:
            // - if other is on exitTileInteraction => always ignore
            // - else if other layer is NOT in allowedMask => ignore
            foreach (var otherCollider in FindObjectsOfType<Collider2D>())
            {
                if (otherCollider == null) continue;
                int otherLayerBit = 1 << otherCollider.gameObject.layer;

                // If collider belongs to an exit tile layer, always ignore collisions with it
                if ((otherLayerBit & exitMask) != 0)
                {
                    Physics2D.IgnoreCollision(myCol, otherCollider, true);
                    continue;
                }

                // If other collider's layer is NOT in allowedMask => ignore collision
                if ((otherLayerBit & allowedMask) == 0)
                {
                    Physics2D.IgnoreCollision(myCol, otherCollider, true);
                }
                // else: keep collisions enabled (we want to collide with this layer)
            }

            // If owner was set before Start, ensure we ignore owner colliders
            ApplyOwnerCollisionIgnore();
        }
        // leave isMoving as-is (Initialize will normally set it true)
        moveTime = 0;
        currentSpeed = baseSpeed;

        activeSpells.Add(this);
    }

    private void OnDestroy()
    {
        UnregisterFromGroup();
    }

    // ---------- Group helpers ----------
    private void CreateNewGroup(int stepLimit)
    {
        int id = s_nextGroupId++;
        var g = new GroupData()
        {
            id = id,
            stepCount = 0,
            lastUpdatedFrame = -1,
            stepLimit = Mathf.Max(1, stepLimit)
        };
        s_groups[id] = g;
        groupId = id;
        g.members.Add(this);
    }

    private void RegisterToGroupIfNeeded()
    {
        if (groupId == UNASSIGNED_GROUP) return;
        if (!s_groups.TryGetValue(groupId, out var g))
        {
            CreateNewGroup(maxGroupSteps);
            return;
        }
        g.members.Add(this);
    }

    private void UnregisterFromGroup()
    {
        if (groupId == UNASSIGNED_GROUP) return;
        if (s_groups.TryGetValue(groupId, out var g))
        {
            g.members.Remove(this);
            if (g.members.Count == 0)
                s_groups.Remove(groupId);
        }
        groupId = UNASSIGNED_GROUP;
    }

    private void DestroyGroup(int gid)
    {
        if (gid == UNASSIGNED_GROUP) return;
        if (!s_groups.TryGetValue(gid, out var g)) return;

        var members = new List<EscalateSpell>(g.members);
        foreach (var m in members)
        {
            if (m != null)
                try { Destroy(m.gameObject); } catch { }
        }
        s_groups.Remove(gid);
    }

    // ---------- Spawn logic ----------
    private void SpawnAroundCenterExact(Vector3 centerWorld)
    {
        for (int i = 0; i < Cardinals.Length; i++)
        {
            Vector2 dir = Cardinals[i];
            Vector3 targetWorld = centerWorld + new Vector3(dir.x * cellSize, dir.y * cellSize, 0f);

            if (HasEscalateAtWorldPosition(targetWorld))
                continue;

            CreateCloneAt(targetWorld, inheritGroup: true);
        }
    }

    private void SpawnAroundInstance(Vector3 centerWorld, int limit)
    {
        if (limit <= 0) return;

        var candidates = new List<Vector3>();
        for (int i = 0; i < Cardinals.Length; i++)
        {
            Vector2 d = Cardinals[i];
            Vector3 pos = centerWorld + new Vector3(d.x * cellSize, d.y * cellSize, 0f);
            if (!HasEscalateAtWorldPosition(pos))
                candidates.Add(pos);
        }

        if (candidates.Count == 0) return;

        Shuffle(candidates);

        int toSpawn = Mathf.Min(limit, candidates.Count);
        for (int i = 0; i < toSpawn; i++)
            CreateCloneAt(candidates[i], inheritGroup: true);
    }

    private void CreateCloneAt(Vector3 worldPos, bool inheritGroup)
    {
        GameObject prefabToUse = spellPrefab != null ? spellPrefab : this.gameObject;
        GameObject clone = Instantiate(prefabToUse, worldPos, Quaternion.identity); // always identity

        var es = clone.GetComponent<EscalateSpell>();
        if (es == null)
        {
            Debug.LogWarning("[EscalateSpell] Instantiated prefab does not contain EscalateSpell component.");
            Destroy(clone);
            return;
        }

        es.suppressInitialSpawn = true;
        es.damageAmount = this.damageAmount;
        es.cellSize = this.cellSize;
        es.spellPrefab = this.spellPrefab;
        es.maxSpawnPerStep = this.maxSpawnPerStep;
        es.maxGroupSteps = this.maxGroupSteps;
        es.defaultSuppressInitialSpawn = this.defaultSuppressInitialSpawn;

        if (inheritGroup && groupId != UNASSIGNED_GROUP)
            es.groupId = this.groupId;

        es.Initialize(Vector3.zero, this.owner);
    }

    // ---------- Turn-step logic ----------
    protected override void MoveFurther()
    {
        if (!this || gameObject == null) return;

        isMoving = false;

        if (groupId == UNASSIGNED_GROUP)
        {
            CreateNewGroup(maxGroupSteps);
            RegisterToGroupIfNeeded();
        }

        if (!s_groups.TryGetValue(groupId, out var g))
            return;

        if (g.lastUpdatedFrame != Time.frameCount)
        {
            g.lastUpdatedFrame = Time.frameCount;
            g.stepCount++;
        }

        if (g.stepCount >= g.stepLimit)
        {
            DestroyGroup(groupId);
            return;
        }

        SpawnAroundInstance(transform.position, maxSpawnPerStep);
    }

    // ---------- Utility ----------
    private const float POS_TOLERANCE = 0.1f;

    private bool HasEscalateAtWorldPosition(Vector3 worldPos)
    {
        var all = FindObjectsOfType<EscalateSpell>();
        foreach (var e in all)
        {
            if (e == null) continue;
            if (Vector3.Distance(e.transform.position, worldPos) <= POS_TOLERANCE)
                return true;
        }
        return false;
    }

    private static void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            var tmp = list[i];
            list[i] = list[j];
            list[j] = tmp;
        }
    }
}
