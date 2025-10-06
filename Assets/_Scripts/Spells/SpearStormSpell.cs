using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// SpearStormSpell:
/// - on Initialize it collects up to `numTargets` random floor tiles from a directional
///   rectangular fan (width x depth) in the direction `dir` relative to the owner,
///   stopping each lateral row when a collider on `whatStopsMovement` is encountered.
/// - spawns prefab copies at the chosen tiles (centered on tile if tilemap provided).
/// - creates a group id and assigns it to spawned targets so they can be cleaned up together.
/// - destroys itself as soon as it finishes creating the targets.
/// </summary>
public class SpearStormSpell : Spell
{
    [Header("Pattern")]
    [Tooltip("Number of lateral cells (odd numbers give a centered pattern, e.g. 3-> -1,0,1).")]
    [SerializeField] private int width = 3; // e.g. 3
    [Tooltip("Depth (how far outward) in tiles.")]
    [SerializeField] private int depth = 8; // e.g. 8
    [Tooltip("How many tiles to choose (random) from the valid candidates.")]
    [SerializeField] private int numTargets = 3;

    [Header("Placement")]
    [Tooltip("World size between tiles (tile size).")]
    [SerializeField] private float cellSize = 1f;
    [Tooltip("Prefab to spawn at each chosen tile.")]
    [SerializeField] private GameObject spawnPrefab;

    [Header("Environment checks")]
    [Tooltip("Tilemap used to determine if a cell contains a floor tile. If set, this is preferred.")]
    [SerializeField] private Tilemap floorTilemap;
    [Tooltip("If no tilemap is set, optionally check for a GameObject with the FloorTag and a collider at the tile cell.")]
    [SerializeField] private bool fallbackCheckByTag = false;
    [Tooltip("Tag to look for in the fallback path-check (requires a collider).")]
    [SerializeField] private string floorTag = "Floor";
    
    [Tooltip("Radius used to check for a stopping collider at a tile center.")]
    [SerializeField] private float checkRadius = 0.1f;

    // small tolerance to compare world positions
    private const float POS_TOLERANCE = 0.1f;

    /// <summary>
    /// Initialize is called by whatever spawner/owner; dir indicates direction of the 'fan'.
    /// We'll collect candidates and spawn immediately upon initialization.
    /// After creating targets we destroy this spawner prefab immediately.
    /// </summary>
    public override void Initialize(Vector3 dir, GameObject owner = null, GameObject sourceDraggable = null, int cooldownTurns = 0)
    {
        // Do not call base.Initialize because it sets isMoving = true in the original Spell.
        // We want this stationary spell to use the owner collision ignore logic but stay immobile.

        // copy owner logic (apply ignore)
        this.owner = owner;
        ApplyOwnerCollisionIgnore();

        // ensure stationary
        Vector2 chosenDir = dir.sqrMagnitude > 1e-6f ? (Vector2)dir.normalized : SnapToCardinalDirection(GlobalDirection.Direction);
        startPosition = transform.position;
        currentSegmentLength = 0f;
        targetPosition = startPosition;

        isMoving = false;
        moveTime = 0f;
        baseSpeed = 0f;
        currentSpeed = 0f;

        // Run the placement (this will instantiate target prefabs, assign them a group id and initial wait)
        TryPlaceTargets(chosenDir, owner);

        // destroy the spawner immediately — it has done its job.
        Destroy(gameObject);
    }

    /// <summary>
    /// Core: gather valid tiles according to direction, stop a lateral row once it hits a wall,
    /// then randomly pick up to numTargets unique tiles and spawn prefabs there.
    /// Also create a group id and distribute wait-turns: first ~1/3 wait 1, next ~1/3 wait 2, rest wait 3.
    /// </summary>
    private void TryPlaceTargets(Vector3 dirWorld, GameObject owner)
    {
        if (numTargets <= 0 || spawnPrefab == null)
        {
            Debug.LogWarning("[SpearStormSpell] No spawn prefab or zero targets.");
            return;
        }

        // Normalize and snap direction to cardinal/diagonal if needed
        Vector2 dir2 = SnapToCardinalDirection(dirWorld);

        // perpendicular (for lateral offsets). For a rightward dir (1,0) perp=(0,1) (up).
        Vector2 perp = new Vector2(-dir2.y, dir2.x);

        // compute lateral offsets list centered at 0
        var lateralOffsets = new List<int>();
        int half = width / 2;
        for (int i = -half; i <= half; i++)
            lateralOffsets.Add(i);

        var candidates = new List<Vector3>();

        Vector3 ownerPos = (owner != null) ? owner.transform.position : transform.position;

        // For each lateral row, step depth times. Stop that row when a wall is encountered.
        foreach (int lat in lateralOffsets)
        {
            for (int d = 1; d <= depth; d++)
            {
                Vector3 candidate = ownerPos + (Vector3)(dir2 * d * cellSize) + (Vector3)(perp * lat * cellSize);

                // 1) if there's a wall/stop collider at candidate -> stop this lateral row (break)
                Collider2D stop = Physics2D.OverlapCircle(candidate, checkRadius, whatStopsMovement);
                if (stop != null)
                {
                    // hit wall or stop tile — do not include this tile and break out of this lateral row
                    break;
                }

                // 2) Check if this tile is a valid floor cell.
                bool isFloor = false;

                // Preferred: use tilemap if provided
                if (floorTilemap != null)
                {
                    Vector3Int cell = floorTilemap.WorldToCell(candidate);
                    var tile = floorTilemap.GetTile(cell);
                    if (tile != null)
                        isFloor = true;
                }
                else if (fallbackCheckByTag)
                {
                    // fallback: detect any collider at point that has the floorTag
                    Collider2D col = Physics2D.OverlapCircle(candidate, checkRadius);
                    if (col != null && col.CompareTag(floorTag))
                        isFloor = true;
                }
                else
                {
                    // no floor detection available, default to considering it valid
                    isFloor = true;
                }

                if (isFloor)
                {
                    // avoid duplicates (very small spatial tolerance)
                    bool duplicate = candidates.Any(c => Vector3.Distance(c, candidate) <= POS_TOLERANCE);
                    if (!duplicate)
                        candidates.Add(candidate);
                }

                // continue this lateral row until depth or hit wall
            }
        }

        if (candidates.Count == 0)
        {
            Debug.Log("[SpearStormSpell] No valid target tiles found.");
            return;
        }

        // shuffle candidates and pick up to numTargets
        Shuffle(candidates);
        int toTake = Mathf.Min(numTargets, candidates.Count);

        // create a new spear-group id for this cast so spawned targets can coordinate cleanup
        int groupId = SpearFallSpell.CreateNewGroup();

        // split chosen spawns into three buckets (round up) for wait-turn distribution
        int remain = toTake;
        int baseCount = toTake / 3;
        int rem = toTake % 3;
        int group0 = baseCount + (rem > 0 ? 1 : 0);
        int group1 = baseCount + (rem > 1 ? 1 : 0);
        int group2 = toTake - group0 - group1;

        // index counters
        int idx = 0;
        int assigned = 0;

        for (int i = 0; i < toTake; i++)
        {
            Vector3 spawnPos = candidates[i];

            // If we have a tilemap, snap to the cell center for perfect alignment
            if (floorTilemap != null)
            {
                Vector3Int cell = floorTilemap.WorldToCell(spawnPos);
                spawnPos = floorTilemap.GetCellCenterWorld(cell);
            }

            // Instantiate the target prefab (this should have SpearFallSpell attached)
            var go = Instantiate(spawnPrefab, spawnPos, Quaternion.identity);
            var s = go.GetComponent<SpearFallSpell>();
            if (s != null)
            {
                // compute which third this index belongs to and assign initial wait turns: 1,2,3
                int wait = 1;
                if (assigned < group0) wait = 1;
                else if (assigned < group0 + group1) wait = 2;
                else wait = 3;
                assigned++;

                // assign group and wait **before** Initialize so the SpearFall registers into the right group
                s.AssignToGroup(groupId);
                s.SetInitialWaitTurns(wait);

                // initialize as stationary target and pass owner
                s.Initialize(Vector3.zero, owner);
            }
            else
            {
                Debug.LogWarning("[SpearStormSpell] Spawned prefab lacks SpearFallSpell component.");
                // still destroy/cleanup won't be possible for these; continue
            }
        }
    }

    // Fisher-Yates shuffle for Vector3 list
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
