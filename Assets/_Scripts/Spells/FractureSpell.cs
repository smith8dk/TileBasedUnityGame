using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// FractureSpell:
/// - Moves one tile per MoveFurther() (tile distance = tileStepSize).
/// - When arriving on a tile it spawns a spike (optional) and a number of fragments
///   randomly chosen within a 5x5 area centered on the arrival tile (center tile excluded).
/// - Fragments will NOT spawn on tiles that overlap whatStopsMovement.
/// - Any child named "FragmentPreview" under this GameObject will be destroyed when the spawn occurs.
/// - The spike disappears after 1 MoveFurther() call (implemented via SpikeLife Spell).
/// - Draws a configurable gizmo grid (gizmoGridSize x gizmoGridSize) to visualize viable/blocked tiles.
/// - Emits a dirt particle burst at start and each time MoveFurther() is called (uses dirtBurstPrefab).
/// </summary>
public class FractureSpell : Spell
{
    [Header("Movement")]
    [Tooltip("World units per tile-step (matches Spell default of 3 by convention).")]
    public float tileStepSize = 3f;

    [Tooltip("How many MoveFurther() calls this spell makes before self-destruction.")]
    public int movesBeforeDestroy = 3;

    [Header("Spawn / Visuals")]
    [Tooltip("Optional spike prefab that appears at the emergence tile (can be null).")]
    public GameObject spikePrefab;

    [Tooltip("Prefab for the fragment tile. Should contain FractureFragment component, SpriteRenderer and Collider2D.")]
    public GameObject fragmentPrefab;

    [Tooltip("How many fragments to spawn (random tiles within the area).")]
    public int fragmentsToSpawn = 5;

    [Header("Fragment behavior")]
    [Tooltip("How many MoveFurther() turns fragments persist before auto-destroy.")]
    public int fragmentLifetimeTurns = 3;

    [Tooltip("Damage applied by each fragment when someone walks on it.")]
    public int fragmentDamage = 2;

    [Tooltip("If true, fragments are destroyed immediately after dealing damage on trigger.")]
    public bool fragmentsDestroyOnStep = true;

    // ---------------- gizmo/grid fields ----------------
    [Header("Gizmo / Grid")]
    [Tooltip("Grid size used by gizmos for preview. (Spawn logic uses 5x5 regardless.)")]
    public int gizmoGridSize = 5;

    [Tooltip("Tile size used by gizmo spacing (world units). If <= 0, defaults to tileStepSize.")]
    public float gizmoTileSize = 0f;
    // ------------------------------------------------------

    [Header("Particles")]
    [Tooltip("ParticleSystem prefab for the dirt bursts. Best if Simulation Space = World. If null, no puffs will be used.")]
    public ParticleSystem dirtBurstPrefab;

    [Tooltip("Number of particles emitted as a local puff when a fragment is spawned or on MoveFurther.")]
    public int fragmentPuffCount = 8;

    // internal tracking
    private int movesTaken = 0;

    // used to spawn fragments when the projectile arrives (after interpolation)
    private bool spawnOnArrival = false;
    private Vector3 spawnPosition;

    // runtime particle instance (single emitter used as a target for Emit calls)
    private ParticleSystem dirtInstance;

    protected override void Start()
    {
        base.Start();
        currentSegmentLength = tileStepSize;

        if (gizmoTileSize <= 0f)
            gizmoTileSize = tileStepSize;

        // create optional dirt emitter and fire an initial burst
        TryCreateDirtEmitter();
        EmitLocalPuff(transform.position, fragmentPuffCount);
    }

    /// <summary>
    /// Instantiate and configure the dirt emitter instance (one per spell).
    /// The prefab is optional; if missing, no emitter is created.
    /// </summary>
    private void TryCreateDirtEmitter()
    {
        if (dirtBurstPrefab == null)
            return;

        dirtInstance = Instantiate(dirtBurstPrefab);
        if (dirtInstance == null)
            return;

        // Ensure the particle system uses world simulation so emitted particles stay where they're emitted.
        var main = dirtInstance.main;
        main.simulationSpace = ParticleSystemSimulationSpace.World;

        // Position emitter at the spell position to start (we keep it available for Emit(...) calls)
        dirtInstance.transform.position = transform.position;

        // Disable automatic emission if prefab emits continuously (we use Emit calls)
        var emission = dirtInstance.emission;
        emission.enabled = false;
    }

    protected override void MoveFurther()
    {
        if (movesTaken >= movesBeforeDestroy)
            return;

        // emit burst at current position whenever MoveFurther is called
        EmitLocalPuff(transform.position, fragmentPuffCount);

        movesTaken++;

        startPosition = transform.position;
        currentSegmentLength = tileStepSize;
        targetPosition = startPosition + (Vector3)(direction.normalized * currentSegmentLength);

        if (movePoint != null)
            movePoint.position = targetPosition;

        isMoving = true;
        moveTime = 0f;

        // schedule spawn at arrival
        spawnOnArrival = true;
        spawnPosition = targetPosition;
    }

    protected override void Update()
    {
        base.Update();

        // keep the emitter positioned with the spell so Emit(...) origins make sense
        if (dirtInstance != null)
        {
            dirtInstance.transform.position = transform.position;
        }

        if (!isMoving && spawnOnArrival)
        {
            spawnOnArrival = false;

            // stop and destroy the persistent emitter (existing particles are allowed to live out)
            StopDirtEmitter();

            // destroy any preview child you might have used for placement (optional)
            DestroyFragmentPreviewChild();

            // spawn spike + random fragments (5x5 area, center excluded)
            SpawnSpikeAndFragments5x5(spawnPosition);

            if (movesTaken >= movesBeforeDestroy)
            {
                Destroy(gameObject);
            }
        }
    }

    private void DestroyFragmentPreviewChild()
    {
        var preview = transform.Find("FragmentPreview");
        if (preview != null)
            Destroy(preview.gameObject);
    }

    /// <summary>
    /// Emit a small burst at worldPosition using the instantiated dirtInstance if available.
    /// If no persistent instance exists, instantiate a one-shot emitter from the prefab.
    /// </summary>
    private void EmitLocalPuff(Vector3 worldPosition, int count)
    {
        if (count <= 0) return;

        if (dirtInstance != null)
        {
            var emitParams = new ParticleSystem.EmitParams();
            emitParams.position = worldPosition;
            dirtInstance.Emit(emitParams, count);
            return;
        }

        if (dirtBurstPrefab != null)
        {
            var tmp = Instantiate(dirtBurstPrefab, worldPosition, Quaternion.identity);
            var main = tmp.main;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            tmp.Play();

            float dur = main.duration;
            float maxLifetime = main.startLifetime.constantMax;
            Destroy(tmp.gameObject, dur + maxLifetime + 0.1f);
        }
    }

    /// <summary>
    /// Stop the persistent dirt emitter and schedule its destruction after remaining particle lifetime.
    /// </summary>
    private void StopDirtEmitter()
    {
        if (dirtInstance == null)
            return;

        // Stop emitting but allow existing particles to live out their lifetime
        dirtInstance.Stop(true, ParticleSystemStopBehavior.StopEmitting);

        var main = dirtInstance.main;
        float dur = main.duration;
        // robustly handle Min/Max lifetime variants
        float maxLifetime = main.startLifetime.constantMax;
        Destroy(dirtInstance.gameObject, dur + maxLifetime + 0.1f);
        dirtInstance = null;
    }

    /// <summary>
    /// NEW: Spawn fragments inside a 5x5 grid centered on 'center', excluding the center tile (which is reserved for the spike).
    /// Choices are filtered by whatStopsMovement and shuffled.
    /// </summary>
    private void SpawnSpikeAndFragments5x5(Vector3 centerRaw)
    {
        Vector3 center = centerRaw;
        var candidates = new List<Vector3>();

        // fixed 5x5 grid: dx,dy in [-2..2]
        int halfExtent = 2;
        float step = Mathf.Max(0.0001f, gizmoTileSize > 0f ? gizmoTileSize : tileStepSize);

        for (int dx = -halfExtent; dx <= halfExtent; dx++)
        {
            for (int dy = -halfExtent; dy <= halfExtent; dy++)
            {
                // skip the exact center tile to avoid overlapping spike
                if (dx == 0 && dy == 0)
                    continue;

                Vector3 pos = center + new Vector3(dx * step, dy * step, 0f);

                // Skip if the tile overlaps whatStopsMovement
                Vector2 checkPoint = new Vector2(pos.x, pos.y);
                Collider2D hit = Physics2D.OverlapPoint(checkPoint, whatStopsMovement);
                if (hit == null)
                {
                    candidates.Add(pos);
                }
            }
        }

        if (candidates.Count == 0)
        {
            Debug.Log("[FractureSpell] No valid tiles available to spawn fragments (all blocked).");
            // Still spawn spike at the exact spell position
            SpawnSpikeAtExactSpellPosition();
            return;
        }

        // Shuffle candidate list (Fisher–Yates)
        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            var tmp = candidates[i];
            candidates[i] = candidates[j];
            candidates[j] = tmp;
        }

#if UNITY_EDITOR
        Debug.Log($"[FractureSpell] Shuffled candidate positions: {string.Join(", ", candidates)}");
#endif

        int toSpawn = Mathf.Clamp(fragmentsToSpawn, 1, candidates.Count);
        for (int i = 0; i < toSpawn; i++)
        {
            SpawnSingleFragment(candidates[i]);
        }

        // Spawn spike AFTER fragments are placed (prevents the spike collider from affecting selection)
        SpawnSpikeAtExactSpellPosition();
    }

    private void SpawnSingleFragment(Vector3 fragPos)
    {
        if (fragmentPrefab == null)
        {
            Debug.LogWarning("FractureSpell: fragmentPrefab not assigned; cannot spawn fragment.");
            return;
        }

        // instantiate fragment at the fracture/spell origin so it can animate outwards
        GameObject fragGO = Instantiate(fragmentPrefab, this.transform.position, Quaternion.identity);

        // Ensure FractureFragment component is present
        FractureFragment fragComp = fragGO.GetComponent<FractureFragment>();
        if (fragComp == null)
            fragComp = fragGO.AddComponent<FractureFragment>();

        // Initialize fragment so it ignores owner and has configured lifetime/damage
        fragComp.InitializeFragment(this.owner, fragmentLifetimeTurns, fragmentDamage, fragmentsDestroyOnStep);

        // tell the fragment where it should end up and how long to take to get there
        // (you can expose this duration on FractureSpell if you want)
        float fragmentPlaceDuration = 0.18f; // tune as needed or add as serialized field
        fragComp.SetTargetPosition(fragPos, fragmentPlaceDuration);

        // Emit a short local puff at the fragment position so the spawn visually pops.
        EmitLocalPuff(fragPos, fragmentPuffCount);
    }

    /// <summary>
    /// Ensures the spike spawns at the exact x,y of this FractureSpell's transform at the moment of spawning.
    /// </summary>
    private void SpawnSpikeAtExactSpellPosition()
    {
        if (spikePrefab == null)
            return;

        GameObject spikeGO = Instantiate(spikePrefab);
        spikeGO.transform.position = new Vector3(transform.position.x, transform.position.y, transform.position.z);

        var spikeLife = spikeGO.GetComponent<SpikeLife>();
        if (spikeLife == null)
            spikeLife = spikeGO.AddComponent<SpikeLife>();

        spikeLife.InitializeSpike(owner);
    }

    protected override void OnTriggerEnter2D(Collider2D other)
    {
        // If we hit a wall, spawn at current position and die
        if ((whatStopsMovement & (1 << other.gameObject.layer)) != 0)
        {
            // stop continuous emitter and spawn at this position
            StopDirtEmitter();
            SpawnSpikeAtExactSpellPosition();
            SpawnSpikeAndFragments5x5(transform.position);
            Destroy(gameObject);
            return;
        }

        // If hits damageable, apply damage and fracture at collision pos
        var damageable = other.GetComponent<IDamageable>();
        if (damageable != null)
        {
            damageable.TakeDamage(damageAmount);
            StopDirtEmitter();
            SpawnSpikeAtExactSpellPosition();
            SpawnSpikeAndFragments5x5(transform.position);
            Destroy(gameObject);
            return;
        }

        // Otherwise use base behaviour (which may destroy the spell)
        base.OnTriggerEnter2D(other);
    }

    // ----------------- GIZMO DEBUG HELPERS -----------------

    private void OnDrawGizmosSelected()
    {
        // choose center: scheduled spawn position (if any) else current position
        Vector3 center = spawnOnArrival ? spawnPosition : transform.position;

        int grid = Mathf.Max(1, gizmoGridSize);
        float step = Mathf.Max(0.0001f, gizmoTileSize > 0f ? gizmoTileSize : tileStepSize);
        float half = (grid / 2f) - 0.5f;
        float cubeSize = step * 0.9f;

        for (int ix = 0; ix < grid; ix++)
        {
            for (int iy = 0; iy < grid; iy++)
            {
                float offsetX = (ix - half) * step;
                float offsetY = (iy - half) * step;
                Vector3 pos = center + new Vector3(offsetX, offsetY, 0f);
                Vector2 checkPoint = new Vector2(pos.x, pos.y);

                Collider2D hit = Physics2D.OverlapPoint(checkPoint, whatStopsMovement);

                if (hit == null)
                {
                    Gizmos.color = new Color(0f, 1f, 0f, 0.35f);
                    Gizmos.DrawCube(pos, new Vector3(cubeSize, cubeSize, 0.01f));
                }
                else
                {
                    Gizmos.color = new Color(1f, 0f, 0f, 0.35f);
                    Gizmos.DrawCube(pos, new Vector3(cubeSize, cubeSize, 0.01f));
                }

                // outline the central nearest tile in blue
                if (Mathf.Abs(offsetX) <= (step * 0.5f) && Mathf.Abs(offsetY) <= (step * 0.5f))
                {
                    Gizmos.color = Color.blue;
                    Gizmos.DrawWireCube(pos, new Vector3(cubeSize, cubeSize, 0.01f));
                }
            }
        }
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();

        // if emitter still exists, ensure it's cleaned up
        if (dirtInstance != null)
        {
            var main = dirtInstance.main;
            float dur = main.duration;
            float maxLifetime = main.startLifetime.constantMax;
            Destroy(dirtInstance.gameObject, dur + maxLifetime + 0.1f);
            dirtInstance = null;
        }
    }
}
