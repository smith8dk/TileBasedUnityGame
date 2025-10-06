using UnityEngine;

/// <summary>
/// AmassSpell (3-row laser):
/// - When initialized it positions itself one tile next to the owner (in the given direction).
/// - Moves exactly one tile each time MoveFurther() is called (suitable for turn-stepped movement).
/// - Destroys itself after completing maxTurns steps.
/// - After a configured number of turns, triggers the Animator parameter "Activate" and stops stepping.
/// - The activation animation should call the public method OnAnimationSpawnLaser() via an Animation Event
///   to spawn three laser parts (top, middle, bottom) which extend independently until they hit whatStopsMovement or maxLaserDistance.
///
/// New: Laser lifetime is configurable via `laserTurnsAlive`. When the lifetime expires the laser parts AND this AmassSpell object are destroyed.
/// </summary>
public class AmassSpell : Spell
{
    [Tooltip("Number of tile-steps before this spell destroys itself (counts completed moves).")]
    public int maxTurns = 3;

    [Tooltip("World units per tile-step. Matches Spell's default step (3 by default in Spell).")]
    public float tileStepSize = 3f;

    [Tooltip("How many completed moves before we switch to the laser animation and stop moving.")]
    public int turnsBeforeActivate = 2;

    // Laser spawn settings (three parts)
    [Tooltip("Prefab for top laser row. If null, laserPrefab will be used as fallback for that row.")]
    public GameObject laserPrefabTop;

    [Tooltip("Prefab for middle laser row. If null, laserPrefab will be used as fallback for that row.")]
    public GameObject laserPrefabMid;

    [Tooltip("Prefab for bottom laser row. If null, laserPrefab will be used as fallback for that row.")]
    public GameObject laserPrefabBottom;

    [Tooltip("Generic fallback prefab if any of the above are null.")]
    public GameObject laserPrefab;

    [Tooltip("Maximum world distance the laser may extend to if nothing blocks it.")]
    public float maxLaserDistance = 20f;

    [Tooltip("World height of a single tile (used to position top/mid/bottom rows).")]
    public float tileWorldSize = 1f;

    [Tooltip("Amount (in world units) that adjacent rows should overlap. Must be >= 0 and < tileWorldSize.")]
    public float verticalOverlap = 0.2f;

    [Tooltip("Optional override for each laser part's vertical size (world units). If <= 0 the prefab's sprite size is used.")]
    public float laserHeightOverride = -1f;

    [Tooltip("How many MoveFurther() calls the spawned laser parts survive before being destroyed.\n" +
             "Set to 1 to destroy on the first MoveFurther() after spawn (default).")]
    public int laserTurnsAlive = 1;

    // how many moves we've taken so far
    private int turnsTaken = 0;

    // once activated we won't step forward anymore
    private bool hasActivated = false;

    // animator controlling the beam sprites
    [Tooltip("Optional: Animator attached to this GameObject (will be fetched automatically if null).")]
    public Animator animator;

    // track spawned laser instances so we can manage them
    private GameObject spawnedTop;
    private GameObject spawnedMid;
    private GameObject spawnedBottom;

    // counts how many MoveFurther() calls have happened since the laser parts were spawned.
    // -1 means no parts currently spawned / tracking disabled.
    private int spawnedTurnsAlive = -1;

    protected override void Start()
    {
        base.Start();
        if (animator == null)
            animator = GetComponent<Animator>();
    }

    public override void Initialize(Vector3 dir, GameObject owner = null, GameObject sourceDraggable = null, int cooldownTurns = 0)
    {
        this.owner = owner;
        ApplyOwnerCollisionIgnore();

        if (dir.sqrMagnitude > 1e-6f)
            direction = dir.normalized;
        else
            direction = SnapToCardinalDirection(GlobalDirection.Direction);

        // rotate to face direction
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);

        if (owner != null)
        {
            Vector3 ownerPos = owner.transform.position;
            transform.position = ownerPos + (Vector3)(direction * tileStepSize);
        }

        if (movePoint == null)
        {
            movePoint = new GameObject("SpellMovepoint").transform;
            movePoint.position = transform.position;
        }

        startPosition = transform.position;
        currentSegmentLength = tileStepSize;
        targetPosition = startPosition + (Vector3)(direction * currentSegmentLength);
        if (movePoint != null)
            movePoint.position = targetPosition;

        isMoving = false;
        moveTime = 0;
        turnsTaken = 0;
        hasActivated = false;

        if (animator != null)
            animator.SetBool("Activate", false);

        // cleanup any previously spawned parts and reset counter
        CleanupSpawnedParts();
    }

    protected override void MoveFurther()
    {
        // If parts are spawned, advance their life counter and destroy when lifetime reached.
        if (hasActivated && (spawnedTop != null || spawnedMid != null || spawnedBottom != null))
        {
            // ensure laserTurnsAlive is at least 1 to avoid immediate destruction unless desired
            int effectiveLifetime = Mathf.Max(1, laserTurnsAlive);

            // increment spawn-age
            if (spawnedTurnsAlive < 0) spawnedTurnsAlive = 0;
            spawnedTurnsAlive++;

            // If we've reached or exceeded configured lifetime, destroy parts now AND destroy this AmassSpell.
            if (spawnedTurnsAlive >= effectiveLifetime)
            {
                if (spawnedTop != null) { Destroy(spawnedTop); spawnedTop = null; }
                if (spawnedMid != null) { Destroy(spawnedMid); spawnedMid = null; }
                if (spawnedBottom != null) { Destroy(spawnedBottom); spawnedBottom = null; }
                spawnedTurnsAlive = -1;

                // Destroy the amass object itself as well.
                Destroy(gameObject);
                // Early return — object is scheduled for destruction.
                return;
            }
            else
            {
                // still within lifetime — do not step forward this turn
                return;
            }
        }

        // If we've activated and there are no parts (already destroyed), keep not moving.
        if (hasActivated)
            return;

        if (turnsTaken >= turnsBeforeActivate)
        {
            if (!hasActivated)
            {
                hasActivated = true;
                if (animator != null)
                    animator.SetBool("Activate", true);
                else
                    Debug.LogWarning("AmassSpell: Animator not assigned - cannot activate laser animation.");

                // Animation Event will call OnAnimationSpawnLaser()
            }
            return;
        }

        if (turnsTaken >= maxTurns)
            return;

        turnsTaken++;

        startPosition = transform.position;
        currentSegmentLength = tileStepSize;
        targetPosition = startPosition + (Vector3)(direction.normalized * currentSegmentLength);
        if (movePoint != null)
            movePoint.position = targetPosition;

        isMoving = true;
        moveTime = 0;
    }

    protected override void Update()
    {
        base.Update();

        if (!isMoving && turnsTaken >= maxTurns)
        {
            Destroy(gameObject);
        }
    }

    protected override void OnTriggerEnter2D(Collider2D other)
    {
        // Defensive: ignore owner's colliders (if somehow OnTrigger fires)
        if (owner != null)
        {
            if (other.gameObject == owner || other.transform.IsChildOf(owner.transform))
                return;
        }

        var damageable = other.GetComponent<IDamageable>();
        if (damageable != null)
        {
            damageable.TakeDamage(damageAmount);
        }

        Debug.Log("Hit for " + damageAmount);
    }

    /// <summary>
    /// Called by an Animation Event at the end of the activation/start animation.
    /// Spawns three laser parts (top, middle, bottom) and extends each independently.
    /// </summary>
    public void OnAnimationSpawnLaser()
    {
        if (!hasActivated)
            hasActivated = true;

        // avoid double spawn
        if (spawnedMid != null || spawnedTop != null || spawnedBottom != null)
            return;

        // perpendicular (local "up" for the beam) — normalized
        Vector2 perp = new Vector2(-direction.y, direction.x).normalized;

        // compute separation between row centers, clamped to >= 0
        float separation = Mathf.Clamp(tileWorldSize - Mathf.Max(0f, verticalOverlap), 0f, tileWorldSize);

        // offsets for rows: top, mid, bottom (centers separated by 'separation')
        Vector2 topOffset = perp * separation;
        Vector2 midOffset = Vector2.zero;
        Vector2 bottomOffset = -perp * separation;

        // spawn each row if prefab available (fallback to laserPrefab when appropriate)
        SpawnLaserPart(GetPrefabForRow(laserPrefabTop), topOffset, ref spawnedTop);
        SpawnLaserPart(GetPrefabForRow(laserPrefabMid), midOffset, ref spawnedMid);
        SpawnLaserPart(GetPrefabForRow(laserPrefabBottom), bottomOffset, ref spawnedBottom);

        // start tracking lifetime (0 means spawned, first MoveFurther will increment to 1)
        spawnedTurnsAlive = 0;
    }

    private GameObject GetPrefabForRow(GameObject specificPrefab)
    {
        if (specificPrefab != null) return specificPrefab;
        return laserPrefab; // may be null
    }

    /// <summary>
    /// Spawns a single laser part at offset (perpendicular) and extends it until whatStopsMovement or maxLaserDistance.
    /// 'spawnedRef' will be assigned to the instantiated GameObject.
    /// Colliders on parts are enabled on spawn.
    /// </summary>
    private void SpawnLaserPart(GameObject prefab, Vector2 perpOffset, ref GameObject spawnedRef)
    {
        if (prefab == null) return; // skip if no prefab

        // Raycast origin is the amass origin shifted by the row offset, plus tiny epsilon forward
        Vector2 origin = (Vector2)transform.position + perpOffset + (Vector2)direction.normalized * 0.01f;
        Vector2 dir2 = direction.normalized;
        RaycastHit2D hit = Physics2D.Raycast(origin, dir2, maxLaserDistance, whatStopsMovement);

        float distance = (hit.collider != null) ? hit.distance : maxLaserDistance;

        // Clamp small positives to avoid behind-origin placement
        const float minDistanceEps = 0.001f;
        distance = Mathf.Max(distance, minDistanceEps);

        // instantiate prefab unparented
        spawnedRef = Instantiate(prefab);
        if (spawnedRef == null) return;

        // compute attach point for this row (near edge)
        Vector3 attachPoint = transform.position + (Vector3)perpOffset;

        // rotate the part to face direction
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        spawnedRef.transform.rotation = Quaternion.Euler(0f, 0f, angle);

        // handle SpriteRenderer sizing & positioning
        var sr = spawnedRef.GetComponent<SpriteRenderer>();
        if (sr != null && sr.sprite != null)
        {
            // pivotNormalizedX: 0 = left edge, 0.5 = center, 1 = right edge (in sprite pixels -> normalized)
            float pivotNormalizedX = 0.5f;
            if (sr.sprite.rect.width > 0f)
                pivotNormalizedX = sr.sprite.pivot.x / sr.sprite.rect.width;

            if (sr.drawMode == SpriteDrawMode.Tiled)
            {
                // set world size: distance x height
                float height = (laserHeightOverride > 0f) ? laserHeightOverride : tileWorldSize;
                sr.size = new Vector2(distance, height);

                // set position so near edge aligns with attachPoint
                spawnedRef.transform.position = attachPoint + (Vector3)(dir2 * (pivotNormalizedX * distance));
            }
            else
            {
                // non-tiled fallback: use sprite.bounds (unscaled) to compute new localScale.x
                float spriteUnscaledWorldWidth = sr.sprite.bounds.size.x;
                if (spriteUnscaledWorldWidth <= 0.0001f) spriteUnscaledWorldWidth = 1f;

                Vector3 originalLocalScale = spawnedRef.transform.localScale;
                float sign = Mathf.Sign(originalLocalScale.x);
                float newLocalScaleX = (distance / spriteUnscaledWorldWidth) * sign;

                spawnedRef.transform.localScale = new Vector3(newLocalScaleX, originalLocalScale.y, originalLocalScale.z);

                // set vertical size if desired (scaling Y) to tileWorldSize (optional)
                if (laserHeightOverride > 0f)
                {
                    float spriteUnscaledWorldHeight = sr.sprite.bounds.size.y;
                    if (spriteUnscaledWorldHeight <= 0.0001f) spriteUnscaledWorldHeight = 1f;
                    float newLocalScaleY = (laserHeightOverride / spriteUnscaledWorldHeight) * Mathf.Sign(originalLocalScale.y);
                    spawnedRef.transform.localScale = new Vector3(spawnedRef.transform.localScale.x, newLocalScaleY, originalLocalScale.z);
                }

                // place pivot location at attachPoint
                spawnedRef.transform.position = attachPoint + (Vector3)(dir2 * (pivotNormalizedX * distance));
            }
        }
        else
        {
            // fallback: center the part at half distance
            spawnedRef.transform.position = attachPoint + (Vector3)(dir2 * (distance * 0.5f));
        }

        // Enable any Collider2D on the spawned part so it can detect hits.
        var colliders = spawnedRef.GetComponentsInChildren<Collider2D>();
        foreach (var c in colliders)
            if (c != null)
                c.enabled = true;

        // parent to AmassSpell so parts remain visually attached (keep world transform)
        spawnedRef.transform.SetParent(transform, true);
    }

    /// <summary>
    /// Clean up spawned parts and reset tracking.
    /// </summary>
    private void CleanupSpawnedParts()
    {
        if (spawnedTop != null) { Destroy(spawnedTop); spawnedTop = null; }
        if (spawnedMid != null) { Destroy(spawnedMid); spawnedMid = null; }
        if (spawnedBottom != null) { Destroy(spawnedBottom); spawnedBottom = null; }
        spawnedTurnsAlive = -1;
    }
}
