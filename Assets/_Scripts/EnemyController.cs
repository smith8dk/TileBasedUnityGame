using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using Pathfinding;

public class EnemyController : MonoBehaviour, IDamageable
{
    [Header("Movement Settings")]
    [SerializeField] private float moveSpeed = 5f;
    [SerializeField] private float sprintMultiplier = 2f;
    [SerializeField] private LayerMask whatStopsMovement;
    [SerializeField] private float detectionRange = 10f;
    [SerializeField] private float distanceThreshold = 1f;
    [SerializeField] private GameObject minimapMarkerPrefab;

    [Header("AI Weights")]
    [SerializeField, Tooltip("Relative chance of choosing to move instead of attacking.")]
    private int moveWeight = 5;

    [Header("Combat (multiple attacks via ScriptableObjects)")]
    [Tooltip("List of available attacks (ScriptableObject assets). The enemy will pick among them.")]
    [SerializeField] private List<EnemyAttackSO> attacks = new List<EnemyAttackSO>();
    [Tooltip("World units per tile (usually 1) for where the attack prefab is spawned relative to enemy tile center.")]
    [SerializeField] private float tileSpawnDistance = 1f;

    // runtime per-attack cooldown counters (parallel to attacks list)
    private int[] attackCooldownRemainingTurns;

    [Header("Loot")]
    public List<LootItem> lootTable = new List<LootItem>();

    [Header("Enemy Stats")]
    [SerializeField] private int maxHealth = 50;
    private int currentHealth;

    private Transform player;
    [Tooltip("Follow point that lags one tile behind the player.")]
    [SerializeField] private Transform followPoint;

    private Seeker seeker;
    private Transform movePoint;
    private Transform target;
    private Path path;
    private int currentWaypoint = 0;
    private bool isMoving = false;
    private GameObject minimapMarker;
    private PlayerInput playerInput;
    private InputAction sprintAction;

    public static List<EnemyController> activeEnemies = new List<EnemyController>();
    public delegate void EnemyTurnComplete();
    public static event EnemyTurnComplete OnEnemyTurnComplete;

    private bool hasDied = false;

    [Header("Knockback Settings")]
    [SerializeField] private float knockbackDuration = 0.2f;
    [SerializeField] private AnimationCurve knockbackCurve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    [Tooltip("Maximum world units the enemy can be knocked back.")]
    [SerializeField] private float maxKnockbackDistance = 3f;
    [Tooltip("Delay before signaling turn complete after knockback ends.")]
    [SerializeField] private float knockbackCompleteDelay = 0.5f;

    private bool isKnockingBack = false;
    private Vector3 knockbackStart;
    private Vector3 knockbackEnd;
    private float knockbackSpeed = 0f;

    private void Awake()
    {
        currentHealth = maxHealth;
    }

    private void Start()
    {
        // 1) Find and cache the player & sprint input
        var playerGO = GameObject.FindGameObjectWithTag("Player");
        if (playerGO != null)
        {
            player = playerGO.transform;
            playerInput = playerGO.GetComponent<PlayerInput>();
            if (playerInput != null)
                sprintAction = playerInput.actions["Sprint"];
        }

        // 2) Find followPoint by name if not assigned
        if (followPoint == null)
        {
            var fpGo = GameObject.Find("FollowPoint");
            if (fpGo != null) followPoint = fpGo.transform;
        }

        // 3) Create movePoint *snapped to tile center*
        movePoint = new GameObject("EnemyMovePoint").transform;
        movePoint.position = SnapToTileCenter(transform.position);

        // 4) Create a dummy target
        target = new GameObject("Target").transform;

        // 5) Spawn minimap marker at same snapped center
        if (minimapMarkerPrefab != null)
        {
            minimapMarker = Instantiate(minimapMarkerPrefab);
            minimapMarker.transform.position = movePoint.position;
        }

        // 6) Grab Seeker for A*
        seeker = GetComponent<Seeker>();
        if (seeker == null) Debug.LogError("Seeker missing on EnemyController.");

        activeEnemies.Add(this);

        // 7) Initial target: follow if in range, else wander
        if (followPoint != null &&
            Vector3.Distance(transform.position, followPoint.position) <= detectionRange)
        {
            target.position = SnapToTileCenter(followPoint.position);
        }
        else
        {
            target.position = SnapToTileCenter(GetRandomFloorPosition());
        }

        // Kick off the first path compute
        UpdatePath();

        // Initialize per-attack cooldown table
        InitializeAttackCooldowns();
    }

    private void InitializeAttackCooldowns()
    {
        attackCooldownRemainingTurns = new int[(attacks != null) ? attacks.Count : 0];
        for (int i = 0; i < attackCooldownRemainingTurns.Length; i++)
            attackCooldownRemainingTurns[i] = 0;
    }

    private void UpdatePath()
    {
        // a) Track followPoint if in detection
        if (followPoint != null &&
            Vector3.Distance(movePoint.position, followPoint.position) <= detectionRange)
        {
            target.position = SnapToTileCenter(followPoint.position);
        }
        else if (Vector3.Distance(movePoint.position, target.position) < distanceThreshold)
        {
            // b) If reached wander target, pick new
            target.position = SnapToTileCenter(GetRandomFloorPosition());
        }

        // c) Ask A* for a new path from current world pos
        if (seeker != null && seeker.IsDone())
            seeker.StartPath(transform.position, target.position, OnPathComplete);
    }

    private void OnPathComplete(Path p)
    {
        if (!p.error)
        {
            path = p;
            currentWaypoint = 0;
        }
    }

    private void FixedUpdate()
    {
        if (isKnockingBack)
        {
            transform.position = Vector3.MoveTowards(
                transform.position,
                knockbackEnd,
                knockbackSpeed * Time.fixedDeltaTime);

            if (Vector3.Distance(transform.position, knockbackEnd) < 0.01f)
            {
                isKnockingBack = false;
                StartCoroutine(KnockbackCompleteCoroutine());
            }
            return;
        }

        if (!isMoving || path == null || currentWaypoint >= path.vectorPath.Count)
            return;

        float speed = moveSpeed;
        if (sprintAction != null && sprintAction.ReadValue<float>() > 0f)
            speed *= sprintMultiplier;

        Vector3 dir = (movePoint.position - transform.position).normalized;
        if (Mathf.Abs(dir.x) > Mathf.Abs(dir.y)) dir.y = 0; else dir.x = 0;
        transform.position += dir * speed * Time.fixedDeltaTime;

        if (minimapMarker != null)
            minimapMarker.transform.position = movePoint.position;

        if (Vector3.Distance(transform.position, movePoint.position) < 0.1f)
        {
            isMoving = false;
            OnEnemyReachedMovePoint();
        }
    }

    private IEnumerator KnockbackCompleteCoroutine()
    {
        yield return new WaitForSeconds(knockbackCompleteDelay);
        OnEnemyTurnComplete?.Invoke();
        UpdatePath();
    }

    private void OnEnemyReachedMovePoint()
    {
        UpdatePath();
        if (activeEnemies.TrueForAll(e => !e.isMoving && !e.isKnockingBack))
            OnEnemyTurnComplete?.Invoke();
    }

    public void EnemyMove()
    {
        if (path == null || path.vectorPath.Count == 0)
        {
            OnEnemyTurnComplete?.Invoke();
            return;
        }

        if (!isMoving)
            currentWaypoint = GetClosestNodeIndex();
        currentWaypoint++;

        if (currentWaypoint >= path.vectorPath.Count)
        {
            OnEnemyTurnComplete?.Invoke();
            return;
        }

        Vector3 currentTileCenter = movePoint.position;
        Vector3 nextPoint = path.vectorPath[currentWaypoint];

        Vector3[] offsets = new Vector3[]
        {
            Vector3.up,
            Vector3.down,
            Vector3.left,
            Vector3.right
        };

        Vector3 bestTile = currentTileCenter;
        float minDistance = float.MaxValue;
        bool foundValidTile = false;

        foreach (var off in offsets)
        {
            Vector3 candidate = currentTileCenter + off;

            // Skip blocked tiles
            if (Physics2D.OverlapCircle(candidate, 0.1f, whatStopsMovement))
                continue;

            float d = Vector3.Distance(candidate, nextPoint);
            if (d < minDistance)
            {
                minDistance = d;
                bestTile = candidate;
                foundValidTile = true;
            }
        }

        if (!foundValidTile || bestTile == currentTileCenter)
        {
            OnEnemyTurnComplete?.Invoke();
            return;
        }

        movePoint.position = bestTile;
        isMoving = true;
    }

    public void EnemyTurn()
    {
        UpdatePath();

        // 1) Tick down cooldowns (consume one enemy turn)
        if (attackCooldownRemainingTurns != null && attackCooldownRemainingTurns.Length > 0)
        {
            for (int i = 0; i < attackCooldownRemainingTurns.Length; i++)
                attackCooldownRemainingTurns[i] = Mathf.Max(0, attackCooldownRemainingTurns[i] - 1);
        }

        // 2) Build candidate action list (attacks that detect player & are off-cooldown; and movement)
        List<(string actionType, int index, int weight)> candidates = new List<(string, int, int)>();

        // --- attacks ---
        if (player != null && !isKnockingBack && attacks != null)
        {
            for (int i = 0; i < attacks.Count; i++)
            {
                var atk = attacks[i];
                if (atk == null || atk.attackPrefab == null) continue;

                // skip if cooldown active
                if (attackCooldownRemainingTurns != null && i < attackCooldownRemainingTurns.Length && attackCooldownRemainingTurns[i] > 0)
                    continue;

                // detection according to this attack's settings (uses SO detection utilities)
                Vector2Int originTile = new Vector2Int(Mathf.FloorToInt((movePoint != null ? movePoint.position.x : transform.position.x)),
                                                       Mathf.FloorToInt((movePoint != null ? movePoint.position.y : transform.position.y)));
                Vector2Int playerTile = new Vector2Int(Mathf.FloorToInt(player.position.x), Mathf.FloorToInt(player.position.y));

                if (atk.DetectPlayerAtOrigin(originTile, playerTile, whatStopsMovement, out Vector2Int primaryDir))
                {
                    int w = Mathf.Max(0, atk.weight);
                    if (w > 0)
                        candidates.Add(("attack", i, w));
                }
            }
        }

        // --- movement ---
        if (path != null && !isMoving && !isKnockingBack && moveWeight > 0)
        {
            candidates.Add(("move", -1, Mathf.Max(0, moveWeight)));
        }

        if (candidates.Count == 0)
        {
            // nothing to do
            OnEnemyTurnComplete?.Invoke();
            return;
        }

        // 3) Weighted random selection
        int totalWeight = 0;
        foreach (var c in candidates) totalWeight += c.weight;

        int roll = Random.Range(0, totalWeight);
        int running = 0;
        (string actionType, int index, int weight) chosen = candidates[0];

        foreach (var c in candidates)
        {
            running += c.weight;
            if (roll < running)
            {
                chosen = c;
                break;
            }
        }

        // 4) Execute chosen action
        if (chosen.actionType == "attack")
        {
            ExecuteAttack(chosen.index);
        }
        else // move
        {
            EnemyMove();
        }
    }

   private void ExecuteAttack(int attackIndex)
    {
        if (attacks == null || attackIndex < 0 || attackIndex >= attacks.Count)
        {
            Debug.LogWarning($"{name}: invalid attackIndex {attackIndex}");
            OnEnemyTurnComplete?.Invoke();
            return;
        }

        var chosen = attacks[attackIndex];

        // Determine origin & player tiles (integer coords)
        Vector3 originWorld = (movePoint != null) ? movePoint.position : SnapToTileCenter(transform.position);
        Vector2Int originTile = new Vector2Int(Mathf.FloorToInt(originWorld.x), Mathf.FloorToInt(originWorld.y));
        Vector3 playerWorld = SnapToTileCenter(player.position);
        Vector2Int playerTile = new Vector2Int(Mathf.FloorToInt(playerWorld.x), Mathf.FloorToInt(playerWorld.y));
        Vector3 enemyTileCenter = SnapToTileCenter(transform.position);

        // Ask the SO to detect player (fills primaryDirInt if available)
        Vector2Int primaryDirInt = Vector2Int.zero;
        // Use the SO helper if available (DetectPlayerAtOrigin),
        // it will set primaryDirInt to a small-int direction toward the player if detected.
        // If your SO uses a different method name, adapt this call accordingly.
        if (chosen != null)
            chosen.DetectPlayerAtOrigin(originTile, playerTile, whatStopsMovement, out primaryDirInt);

        // Compute spawn position based on SO.spawnTarget
        Vector3 spawnPos = enemyTileCenter; // default
        Vector3 spawnDir = Vector3.zero;    // direction passed to Spell.Initialize

        if (chosen.spawnTarget == EnemyAttackSO.SpawnTarget.EnemyTile)
        {
            spawnPos = enemyTileCenter;

            // fallback direction: compute from enemy -> player
            if (primaryDirInt == Vector2Int.zero)
            {
                Vector3 dirToPlayer = (playerWorld - enemyTileCenter).normalized;
                spawnDir = chosen.allowEightDirections ? SnapToEightWay(dirToPlayer) : SnapToCardinal(dirToPlayer);
            }
            else
            {
                spawnDir = new Vector3(primaryDirInt.x, primaryDirInt.y, 0f);
            }
        }
        else if (chosen.spawnTarget == EnemyAttackSO.SpawnTarget.PlayerTile)
        {
            spawnPos = playerWorld;
            Vector3 dirToPlayer = (playerWorld - enemyTileCenter).normalized;
            spawnDir = chosen.allowEightDirections ? SnapToEightWay(dirToPlayer) : SnapToCardinal(dirToPlayer);
        }
        else // AdjacentToPlayer
        {
            // Build neighbor offsets (prefer 8 if allowed) and sort them by distance to enemy
            List<Vector2Int> neighborOffsets = new List<Vector2Int>
            {
                new Vector2Int(0,1),
                new Vector2Int(1,0),
                new Vector2Int(0,-1),
                new Vector2Int(-1,0)
            };
            if (chosen.allowEightDirections)
            {
                neighborOffsets.Add(new Vector2Int(1,1));
                neighborOffsets.Add(new Vector2Int(1,-1));
                neighborOffsets.Add(new Vector2Int(-1,1));
                neighborOffsets.Add(new Vector2Int(-1,-1));
            }

            neighborOffsets.Sort((a, b) =>
            {
                Vector3 wa = new Vector3(playerTile.x + a.x + 0.5f, playerTile.y + a.y + 0.5f, 0f);
                Vector3 wb = new Vector3(playerTile.x + b.x + 0.5f, playerTile.y + b.y + 0.5f, 0f);
                float da = Vector3.SqrMagnitude(wa - enemyTileCenter);
                float db = Vector3.SqrMagnitude(wb - enemyTileCenter);
                return da.CompareTo(db);
            });

            const float checkRadius = 0.1f;
            bool found = false;
            foreach (var off in neighborOffsets)
            {
                Vector2Int candidateTile = playerTile + off;
                Vector3 candidateWorld = new Vector3(candidateTile.x + 0.5f, candidateTile.y + 0.5f, 0f);

                if (!Physics2D.OverlapCircle(candidateWorld, checkRadius, whatStopsMovement))
                {
                    spawnPos = candidateWorld;
                    found = true;
                    Vector3 dirToPlayer = (playerWorld - enemyTileCenter).normalized;
                    spawnDir = chosen.allowEightDirections ? SnapToEightWay(dirToPlayer) : SnapToCardinal(dirToPlayer);
                    break;
                }
            }

            if (!found)
            {
                if (chosen.fallbackToPlayerIfNoAdjacent)
                {
                    spawnPos = playerWorld;
                    Vector3 dirToPlayer = (playerWorld - enemyTileCenter).normalized;
                    spawnDir = chosen.allowEightDirections ? SnapToEightWay(dirToPlayer) : SnapToCardinal(dirToPlayer);
                }
                else
                {
                    spawnPos = enemyTileCenter;
                    if (primaryDirInt == Vector2Int.zero)
                    {
                        Vector3 dirToPlayer = (playerWorld - enemyTileCenter).normalized;
                        spawnDir = chosen.allowEightDirections ? SnapToEightWay(dirToPlayer) : SnapToCardinal(dirToPlayer);
                    }
                    else
                    {
                        spawnDir = new Vector3(primaryDirInt.x, primaryDirInt.y, 0f);
                    }
                }
            }
        }

        // Final safety check: if spawn tile is blocked, fallback to enemy tile center
        const float finalCheckRadius = 0.1f;
        if (Physics2D.OverlapCircle(spawnPos, finalCheckRadius, whatStopsMovement))
        {
            Debug.LogWarning($"{name}: chosen spawn tile {spawnPos} blocked; falling back to enemy tile.");
            spawnPos = enemyTileCenter;
        }

        // Compute rotation for prefab
        float angle = Mathf.Atan2(spawnDir.y, spawnDir.x) * Mathf.Rad2Deg + chosen.prefabRotationOffset;
        Quaternion rot = Quaternion.Euler(0f, 0f, angle);

        // Instantiate and initialize
        GameObject go = null;
        if (chosen.attackPrefab != null)
        {
            go = Instantiate(chosen.attackPrefab, spawnPos, rot);
            if (go != null)
            {
                Spell spell = go.GetComponent<Spell>();
                if (spell != null)
                {
                    Vector3 initDir = (spawnDir == Vector3.zero) ? Vector3.up : spawnDir;
                    initDir = new Vector3(Mathf.Round(initDir.x), Mathf.Round(initDir.y), 0f);
                    spell.Initialize(initDir, this.gameObject);
                }
                else
                {
                    Debug.LogWarning($"{name}: spawned prefab has no Spell component.");
                }
            }
        }
        else
        {
            Debug.LogWarning($"{name}: chosen attack prefab is null.");
        }

        // Put chosen attack on cooldown
        if (attackCooldownRemainingTurns != null && attackIndex < attackCooldownRemainingTurns.Length)
            attackCooldownRemainingTurns[attackIndex] = Mathf.Max(0, chosen.cooldownTurns);

        Debug.Log($"{name} used attack '{chosen.attackName}' at {spawnPos} (spawnTarget={chosen.spawnTarget})");
        OnEnemyTurnComplete?.Invoke();
    }

    // Snap to four cardinal directions
    private Vector3 SnapToCardinal(Vector3 v)
    {
        if (v.sqrMagnitude < 1e-6f) return Vector3.right;
        float angle = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
        if (angle > -45f && angle <= 45f) return Vector3.right;
        if (angle > 45f && angle <= 135f) return Vector3.up;
        if (angle <= -45f && angle > -135f) return Vector3.down;
        return Vector3.left;
    }

    // Snap to eight directions (N,NE,E,SE,S,SW,W,NW)
    private Vector3 SnapToEightWay(Vector3 v)
    {
        if (v.sqrMagnitude < 1e-6f) return Vector3.right;
        float angle = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
        float snapped = Mathf.Round(angle / 45f) * 45f;
        float r = snapped * Mathf.Deg2Rad;
        return new Vector3(Mathf.Round(Mathf.Cos(r)), Mathf.Round(Mathf.Sin(r)), 0f);
    }

    private bool ApproximatelySameTile(Vector3 a, Vector3 b)
    {
        const float eps = 0.01f;
        return Mathf.Abs(a.x - b.x) < eps && Mathf.Abs(a.y - b.y) < eps;
    }

    public void HandleKnockback()
    {
        if (movePoint == null || player == null) return;

        Vector3 diff = transform.position - player.position;
        float angle = Mathf.Atan2(diff.y, diff.x) * Mathf.Rad2Deg;
        float snappedAngle = Mathf.Round(angle / 45f) * 45f;
        Vector3 knockDir = new Vector3(
            Mathf.Cos(snappedAngle * Mathf.Deg2Rad),
            Mathf.Sin(snappedAngle * Mathf.Deg2Rad),
            0f);

        float traveled = 0f, step = 1f;
        Vector3 targetPos = movePoint.position;
        Vector3 next = targetPos + knockDir * step;

        while (traveled < maxKnockbackDistance &&
               !Physics2D.OverlapCircle(next, 0.1f, whatStopsMovement))
        {
            targetPos = next;
            next += knockDir * step;
            traveled += step;
        }

        movePoint.position = targetPos;
        knockbackStart = transform.position;
        knockbackEnd = targetPos;

        float dist = Vector3.Distance(knockbackStart, knockbackEnd);
        knockbackSpeed = (knockbackDuration > 0f) ? dist / knockbackDuration : float.MaxValue;
        isKnockingBack = true;
        isMoving = false;
        path = null;

        if (minimapMarker != null)
            minimapMarker.transform.position = movePoint.position;
    }

    public void TakeDamage(int amount)
    {
        if (hasDied) return;
        currentHealth -= amount;
        if (currentHealth <= 0) HandleDeath();
    }

    private void HandleDeath()
    {
        if (hasDied) return;
        hasDied = true;

        OnEnemyTurnComplete?.Invoke();
        activeEnemies.Remove(this);
        CancelInvoke(nameof(UpdatePath));

        foreach (var loot in lootTable)
            if (Random.value <= loot.dropChance)
                Instantiate(loot.itemPrefab, transform.position, Quaternion.identity);

        if (minimapMarker != null) Destroy(minimapMarker);
        if (movePoint != null) Destroy(movePoint.gameObject);
        if (target != null) Destroy(target.gameObject);

        Destroy(gameObject);
    }

    private int GetClosestNodeIndex()
    {
        if (path == null || path.vectorPath == null || path.vectorPath.Count == 0)
            return 0;

        float minDist = float.MaxValue;
        int idx = 0;
        for (int i = 0; i < path.vectorPath.Count; i++)
        {
            float d = Vector3.Distance(transform.position, path.vectorPath[i]);
            if (d < minDist)
            {
                minDist = d;
                idx = i;
            }
        }
        return idx;
    }

    private Vector3 GetRandomFloorPosition()
    {
        var gen = FindObjectOfType<CorridorFirstDungeonGenerator>();
        var floors = gen?.GetFloorTilePositions();
        if (floors == null || floors.Count == 0)
            return transform.position;

        var list = new List<Vector2Int>(floors);
        var choice = list[Random.Range(0, list.Count)];
        return new Vector3(choice.x + 0.5f, choice.y + 0.5f, 0f);
    }

    public static void DestroyAllMinimapMarkersAndTargets()
    {
        foreach (var enemy in activeEnemies)
        {
            if (enemy == null) continue;
            if (enemy.minimapMarker != null) Destroy(enemy.minimapMarker);
            if (enemy.movePoint != null) Destroy(enemy.movePoint.gameObject);
            if (enemy.target != null) Destroy(enemy.target.gameObject);
        }
    }

    public static void InvokeOnEnemyTurnComplete()
    {
        // No automatic cooldown tick here — the PlayerController.OnEnemyTurnComplete()
        // is the single place that ticks cooldowns for the whole system.
        OnEnemyTurnComplete?.Invoke();
    }


    /// <summary>
    /// Snap any world position to the nearest tile center,
    /// assuming tiles are 1×1 units and centered at n+0.5f.
    /// </summary>
    private Vector3 SnapToTileCenter(Vector3 worldPos)
    {
        float x = Mathf.Floor(worldPos.x) + 0.5f;
        float y = Mathf.Floor(worldPos.y) + 0.5f;
        return new Vector3(x, y, 0f);
    }

    #region Editor gizmo preview for attack ranges

    private void OnDrawGizmosSelected()
    {
        if (attacks == null || attacks.Count == 0) return;

        Vector3 originWorld = (movePoint != null) ? movePoint.position : SnapToTileCenter(transform.position);

        if (float.IsNaN(originWorld.x) || float.IsNaN(originWorld.y)) return;

        for (int i = 0; i < attacks.Count; i++)
        {
            var atk = attacks[i];
            if (atk == null) continue;

            Color baseCol = ColorFromIndex(i);
            Color fill = new Color(baseCol.r, baseCol.g, baseCol.b, 0.20f);
            Color edge = new Color(baseCol.r * 0.85f, baseCol.g * 0.85f, baseCol.b * 0.85f, 0.9f);

            DrawAttackPreview(atk, originWorld, fill, edge, i);
        }

        Gizmos.color = Color.white;
        Vector3 center = SnapToTileCenter(originWorld);
        Gizmos.DrawWireCube(center, Vector3.one * 0.98f);
    }

    private void DrawAttackPreview(EnemyAttackSO atk, Vector3 originWorld, Color fillColor, Color edgeColor, int index)
    {
        if (atk == null) return;

        if (atk.shapeType == EnemyAttackSO.ShapeType.OffsetList)
        {
            Vector2Int originTile = new Vector2Int(Mathf.FloorToInt(originWorld.x), Mathf.FloorToInt(originWorld.y));
            foreach (var off in atk.offsets)
            {
                // rotate the offset according to allowed facings and draw all orientations so designer can see pattern
                var facings = new List<Vector2Int>() { new Vector2Int(0,1) };
                facings.AddRange(atk.allowEightDirections ? new List<Vector2Int>{ new Vector2Int(1,0), new Vector2Int(0,-1), new Vector2Int(-1,0),
                                                                                 new Vector2Int(1,1), new Vector2Int(1,-1), new Vector2Int(-1,1), new Vector2Int(-1,-1)}
                                                        : new List<Vector2Int>{ new Vector2Int(1,0), new Vector2Int(0,-1), new Vector2Int(-1,0)});
                foreach (var facing in facings)
                {
                    Vector2Int rotated = RotateOffsetForPreview(off, facing);
                    Vector2Int tile = originTile + rotated;
                    Vector3 world = new Vector3(tile.x + 0.5f, tile.y + 0.5f, 0f);
                    Gizmos.color = fillColor;
                    Gizmos.DrawCube(world, Vector3.one * 0.98f);
                    Gizmos.color = edgeColor;
                    Gizmos.DrawWireCube(world, Vector3.one * 0.98f);
                }
            }
            return;
        }

        // For Line/Circle use existing preview helper (mirrors runtime scanning)
        int maxSteps = atk.rangeSteps;
        int width = atk.rangeWidth;
        bool allow8 = atk.allowEightDirections;

        if (maxSteps <= 0) return;
        int w = Mathf.Max(1, width);
        int half = w / 2;
        Vector2Int originTile2 = new Vector2Int(Mathf.FloorToInt(originWorld.x), Mathf.FloorToInt(originWorld.y));

        List<Vector2Int> dirs = new List<Vector2Int> {
            new Vector2Int(0,1), new Vector2Int(0,-1), new Vector2Int(-1,0), new Vector2Int(1,0)
        };
        if (allow8)
        {
            dirs.Add(new Vector2Int(1,1)); dirs.Add(new Vector2Int(1,-1)); dirs.Add(new Vector2Int(-1,1)); dirs.Add(new Vector2Int(-1,-1));
        }

        const float checkRadius = 0.1f;
        foreach (var d in dirs)
        {
            Vector2Int perp = new Vector2Int(d.y, -d.x);
            bool[] blockedCols = new bool[w];

            for (int step = 1; step <= maxSteps; step++)
            {
                for (int wi = 0; wi < w; wi++)
                {
                    if (blockedCols[wi]) continue;

                    int lateral = wi - half;
                    Vector2Int candidateTile = originTile2 + d * step + perp * lateral;
                    Vector3 candidateWorld = new Vector3(candidateTile.x + 0.5f, candidateTile.y + 0.5f, 0f);

                    bool blocked = atk.stopsOnWalls && Physics2D.OverlapCircle(candidateWorld, checkRadius, whatStopsMovement);
                    if (blocked)
                    {
                        blockedCols[wi] = true;
                        Gizmos.color = new Color(0f, 0f, 0f, 0.35f);
                        Gizmos.DrawCube(candidateWorld, Vector3.one * 0.98f);
                        Gizmos.color = Color.black;
                        Gizmos.DrawWireCube(candidateWorld, Vector3.one * 0.98f);
                        continue;
                    }

                    Gizmos.color = fillColor;
                    Gizmos.DrawCube(candidateWorld, Vector3.one * 0.98f);
                    Gizmos.color = edgeColor;
                    Gizmos.DrawWireCube(candidateWorld, Vector3.one * 0.98f);
                }

                bool allColsBlocked = true;
                for (int i = 0; i < w; i++)
                    if (!blockedCols[i]) { allColsBlocked = false; break; }
                if (allColsBlocked) break;
            }
        }
    }

    private Vector2Int RotateOffsetForPreview(Vector2Int offset, Vector2Int facing)
    {
        // Use same rotation logic as SO.RotateOffset (but replicated here for preview independence)
        if (facing == new Vector2Int(0, 1)) return offset;
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

    private Color ColorFromIndex(int i)
    {
        Color[] pal = new Color[]
        {
            new Color(0.85f, 0.2f, 0.2f),
            new Color(0.2f, 0.85f, 0.2f),
            new Color(0.2f, 0.45f, 0.85f),
            new Color(0.85f, 0.6f, 0.2f),
            new Color(0.6f, 0.2f, 0.85f),
            new Color(0.2f, 0.85f, 0.7f),
        };
        return pal[i % pal.Length];
    }

    #endregion
}
