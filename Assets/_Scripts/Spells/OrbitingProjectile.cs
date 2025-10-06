using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class OrbitingProjectile : Spell
{
    [Tooltip("Optional target Transform (used only to capture the initial anchor position).")]
    public Transform target;

    [Tooltip("Current angular position (degrees) around the anchor.")]
    public float angleDegrees = 0f;

    [Tooltip("Orbit radius (world units).")]
    public float radius = 1f;

    [Tooltip("Degrees to rotate around the anchor each MoveFurther() (per-turn rotation).")]
    public float rotateDegreesPerTurn = 45f;

    [Tooltip("Number of MoveFurther() calls to live for. When reached, the shard destroys itself.")]
    public int lifetimeTurns = 3;

    // internal counter
    private int turnsLived = 0;

    // When true we will destroy the shard after the currently running interpolation completes.
    private bool destroyAfterCurrentMove = false;

    // Fixed anchor point captured when the shard is spawned (target.position at spawn time).
    private Vector3 anchorPosition;

    protected override void Start()
    {
        base.Start();
        isMoving = false;
        moveTime = 0f;
    }

    /// <summary>
    /// Initialize orbit parameters when spawned programmatically.
    /// Captures the target's current position as the fixed anchorPosition.
    /// </summary>
    public void InitializeOrbit(Transform target, float initialAngleDeg, float orbitRadius, float degPerTurn, int lifeTurns, GameObject owner = null, int inheritedDamage = 0)
    {
        this.target = target; // kept for reference, but NOT used for orbiting after this point
        this.angleDegrees = initialAngleDeg;
        this.radius = orbitRadius;
        this.rotateDegreesPerTurn = degPerTurn;
        this.lifetimeTurns = Mathf.Max(1, lifeTurns);
        this.turnsLived = 0;
        this.destroyAfterCurrentMove = false;

        // capture anchor from the target's current position (if target is null, use current shard position)
        if (target != null)
            anchorPosition = target.position;
        else
            anchorPosition = transform.position;

        // keep same z as anchor (preserve z)
        anchorPosition.z = transform.position.z;

        // Inherit damage if provided
        if (inheritedDamage > 0)
            this.damageAmount = inheritedDamage;

        // Set owner (protected field) via this class (allowed) and apply collision-ignore with owner colliders
        if (owner != null)
        {
            this.owner = owner;
            ApplyOwnerCollisionIgnore();
        }

        // Place at initial position immediately using anchor
        UpdatePositionFromAngle();
    }

    /// <summary>
    /// Smoothly move to the next angular position using Spell's interpolation.
    /// Uses a fixed anchorPosition captured at spawn time.
    /// </summary>
    protected override void MoveFurther()
    {
        // Compute the next angle and target position (anchorPosition used instead of target.position)
        float nextAngle = angleDegrees + rotateDegreesPerTurn;
        nextAngle = (nextAngle % 360f + 360f) % 360f;

        float rad = nextAngle * Mathf.Deg2Rad;
        Vector3 offset = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f) * radius;
        Vector3 nextPos = anchorPosition + offset;

        startPosition = transform.position;
        targetPosition = nextPos;

        currentSegmentLength = Vector3.Distance(startPosition, targetPosition);
        if (currentSegmentLength <= 1e-4f)
            currentSegmentLength = 1e-4f;

        if (movePoint != null)
            movePoint.position = targetPosition;

        isMoving = true;
        moveTime = 0f;

        // commit the angle for internal state
        angleDegrees = nextAngle;

        // Count this turn — schedule destruction after interpolation if lifetime reached.
        turnsLived++;
        if (turnsLived >= lifetimeTurns)
            destroyAfterCurrentMove = true;
    }

    protected override void Update()
    {
        base.Update();

        // If flagged to destroy after current interpolation, wait for movement to finish then destroy
        if (destroyAfterCurrentMove && !isMoving)
            Destroy(gameObject);
    }

    private void UpdatePositionFromAngle()
    {
        float rad = angleDegrees * Mathf.Deg2Rad;
        Vector3 offset = new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f) * radius;
        transform.position = anchorPosition + offset;
    }

    /// <summary>
    /// Proper trigger handling: ignores owner, destroys on wall hits (whatStopsMovement), applies IDamageable damage.
    /// </summary>
    protected override void OnTriggerEnter2D(Collider2D other)
    {
        // Defensive: ignore owner's colliders
        if (owner != null)
        {
            if (other.gameObject == owner || other.transform.IsChildOf(owner.transform))
                return;
        }

        // Wall / blocking check: destroy on contact with whatStopsMovement
        if ((whatStopsMovement & (1 << other.gameObject.layer)) != 0)
        {
            Debug.Log($"OrbitingProjectile: hit wall '{other.name}' — destroying shard.");
            Destroy(gameObject);
            return;
        }

        var damageable = other.GetComponent<IDamageable>();
        if (damageable != null)
        {
            damageable.TakeDamage(damageAmount);
        }

        Debug.Log("Hit for " + damageAmount);

        Destroy(gameObject);

        // Fallback to base behavior
        base.OnTriggerEnter2D(other);
    }
}
