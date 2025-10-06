using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Data-only ScriptableObject describing an enemy attack:
/// - attackName, prefab, weight, cooldown (turns)
/// - shape definition (offset list, line, circle)
/// - behavior flags (allowEightDirections, stopsOnWalls)
/// 
/// Also exposes helper detection code so EnemyController can ask:
///   Does this attack detect the player from origin? If yes, return the primary direction.
/// </summary>
[CreateAssetMenu(fileName = "EnemyAttack", menuName = "Enemy/Attack", order = 100)]
public class EnemyAttackSO : ScriptableObject
{
    public enum ShapeType
    {
        OffsetList,
        Line,
        Circle
    }

    [Header("Identity")]
    public string attackName = "NewAttack";
    public GameObject attackPrefab;
    [Tooltip("Relative likelihood weight for AI selection (0 = never chosen)")]
    public int weight = 1;
    [Tooltip("Turns of cooldown after using this attack (turn-based).")]
    public int cooldownTurns = 1;

    [Header("Prefab Orientation")]
    [Tooltip("Rotation offset (degrees) to apply to prefab so its 'up' faces direction.")]
    public float prefabRotationOffset = -90f;
    [Tooltip("If false, the prefab will spawn with its default rotation, ignoring direction.")]
    public bool applyRotation = true;

    [Header("Detection / Shape")]
    public ShapeType shapeType = ShapeType.Line;

    [Tooltip("Used by Line/Circle: how many tile steps outward.")]
    public int rangeSteps = 5;

    [Tooltip("Used by Line: how wide the line is (in tiles) centered on main axis).")]
    public int rangeWidth = 1;

    [Tooltip("If true, this attack may be fired in diagonal directions as well as cardinal.")]
    public bool allowEightDirections = false;

    [Tooltip("If true, walls (whatStopsMovement) will block this attack's detection along columns/lines.")]
    public bool stopsOnWalls = true;

    [Header("Offset list (manual shape authoring)")]
    [Tooltip("List of relative tile offsets (center = (0,0)). These are used when ShapeType == OffsetList.")]
    public List<Vector2Int> offsets = new List<Vector2Int>();

    // --------------------
    // Runtime helpers
    // --------------------

    public enum SpawnTarget
    {
        EnemyTile,      // spawn at enemy tile (existing behavior)
        PlayerTile,     // spawn ON the player's tile
        AdjacentToPlayer// spawn on a free tile adjacent to the player (preferred)
    }

    [Header("Spawn behavior")]
    [Tooltip("Where the attack prefab should be spawned")]
    public SpawnTarget spawnTarget = SpawnTarget.EnemyTile;

    [Tooltip("If no adjacent player tile is free, fall back to spawning on the player's tile if true, otherwise fallback to enemy tile.")]
    public bool fallbackToPlayerIfNoAdjacent = true;

    /// <summary>
    /// Ask whether this attack's shape (when oriented appropriately) reaches the player's tile.
    /// originTile and playerTile are integer tile coords (tile center at x + 0.5, y + 0.5).
    /// whatStopsMovement: pass EnemyController.whatStopsMovement so we can perform wall checks.
    /// Returns true if detected; outPrimaryDirection is the small-integer direction to use for spawning (e.g. (0,1), (1,0), (1,1)).
    /// </summary>
    public bool DetectPlayerAtOrigin(
        Vector2Int originTile,
        Vector2Int playerTile,
        LayerMask whatStopsMovement,
        out Vector2Int outPrimaryDirection)
    {
        outPrimaryDirection = Vector2Int.zero;

        // same tile
        if (originTile == playerTile)
        {
            outPrimaryDirection = Vector2Int.zero;
            return true;
        }

        switch (shapeType)
        {
            case ShapeType.OffsetList:
                return DetectUsingOffsetList(originTile, playerTile, whatStopsMovement, out outPrimaryDirection);

            case ShapeType.Line:
                return DetectUsingLine(originTile, playerTile, whatStopsMovement, out outPrimaryDirection);

            case ShapeType.Circle:
                return DetectUsingCircle(originTile, playerTile, out outPrimaryDirection);

            default:
                return false;
        }
    }

    private bool DetectUsingOffsetList(
        Vector2Int originTile,
        Vector2Int playerTile,
        LayerMask whatStopsMovement,
        out Vector2Int outPrimaryDirection)
    {
        outPrimaryDirection = Vector2Int.zero;
        if (offsets == null || offsets.Count == 0) return false;

        var facings = GetAllowedFacings();

        foreach (var facing in facings)
        {
            foreach (var off in offsets)
            {
                Vector2Int rotated = RotateOffset(off, facing);
                Vector2Int candidate = originTile + rotated;

                if (stopsOnWalls)
                {
                    Vector3 world = new Vector3(candidate.x + 0.5f, candidate.y + 0.5f, 0f);
                    if (Physics2D.OverlapCircle(world, 0.1f, whatStopsMovement))
                        continue;
                }

                if (candidate == playerTile)
                {
                    outPrimaryDirection = NormalizeDirection(rotated);
                    return true;
                }
            }
        }

        return false;
    }

    private bool DetectUsingLine(
        Vector2Int originTile,
        Vector2Int playerTile,
        LayerMask whatStopsMovement,
        out Vector2Int outPrimaryDirection)
    {
        outPrimaryDirection = Vector2Int.zero;
        var facings = GetAllowedFacings();

        foreach (var d in facings)
        {
            Vector2Int perp = new Vector2Int(d.y, -d.x);
            int w = Math.Max(1, rangeWidth);
            bool[] blocked = new bool[w];
            int half = rangeWidth / 2;

            for (int step = 1; step <= rangeSteps; step++)
            {
                bool anyOpenThisStep = false;

                for (int wi = 0; wi < rangeWidth; wi++)
                {
                    if (blocked[wi]) continue;

                    int lateral = wi - half;
                    Vector2Int candidate = originTile + d * step + perp * lateral;

                    if (stopsOnWalls)
                    {
                        Vector3 world = new Vector3(candidate.x + 0.5f, candidate.y + 0.5f, 0f);
                        if (Physics2D.OverlapCircle(world, 0.1f, whatStopsMovement))
                        {
                            blocked[wi] = true;
                            continue;
                        }
                    }

                    if (candidate == playerTile)
                    {
                        outPrimaryDirection = d;
                        return true;
                    }

                    anyOpenThisStep = true;
                }

                bool allBlocked = true;
                for (int i = 0; i < rangeWidth; i++)
                    if (!blocked[i]) { allBlocked = false; break; }
                if (allBlocked) break;
            }
        }

        return false;
    }

    private bool DetectUsingCircle(
        Vector2Int originTile,
        Vector2Int playerTile,
        out Vector2Int outPrimaryDirection)
    {
        outPrimaryDirection = Vector2Int.zero;

        int r = Mathf.Max(0, rangeSteps);
        int r2 = r * r;

        Vector2Int diff = playerTile - originTile;
        if (diff.x * diff.x + diff.y * diff.y <= r2)
        {
            outPrimaryDirection = NormalizeDirection(diff);
            return true;
        }

        return false;
    }

    // --------------------
    // Utilities
    // --------------------

    private List<Vector2Int> GetAllowedFacings()
    {
        var list = new List<Vector2Int>
        {
            new Vector2Int(0, 1),
            new Vector2Int(0, -1),
            new Vector2Int(-1, 0),
            new Vector2Int(1, 0)
        };

        if (allowEightDirections)
        {
            list.Add(new Vector2Int(1, 1));
            list.Add(new Vector2Int(1, -1));
            list.Add(new Vector2Int(-1, 1));
            list.Add(new Vector2Int(-1, -1));
        }

        return list;
    }

    private Vector2Int RotateOffset(Vector2Int offset, Vector2Int facing)
    {
        if (facing == Vector2Int.up) return offset;

        float angleFromUp = Mathf.Atan2(facing.y, facing.x) * Mathf.Rad2Deg - 90f;
        float rad = angleFromUp * Mathf.Deg2Rad;
        float ca = Mathf.Cos(rad);
        float sa = Mathf.Sin(rad);

        float rx = offset.x * ca - offset.y * sa;
        float ry = offset.x * sa + offset.y * ca;

        int ix = Mathf.RoundToInt(rx);
        int iy = Mathf.RoundToInt(ry);
        return new Vector2Int(ix, iy);
    }

    private Vector2Int NormalizeDirection(Vector2Int v)
    {
        if (v == Vector2Int.zero) return Vector2Int.zero;
        int sx = (v.x > 0) ? 1 : (v.x < 0 ? -1 : 0);
        int sy = (v.y > 0) ? 1 : (v.y < 0 ? -1 : 0);
        return new Vector2Int(sx, sy);
    }
}
