using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class FractureFragment : Spell
{
    [Tooltip("How many MoveFurther() turns this fragment lives before auto-destroy.")]
    public int lifetimeTurns = 3;

    [Tooltip("Damage dealt when someone walks on the fragment.")]
    public int damageAmountOnStep = 1;

    [Tooltip("If true, fragment destroys itself immediately after dealing damage on trigger.")]
    public bool destroyOnStep = true;

    [Header("Placement")]
    [Tooltip("How long (seconds) the fragment takes to lerp from spawn -> target tile.")]
    public float placeDuration = 0.18f;

    // runtime state
    private int turnsAlive = 0;
    private bool placed = true; // assume placed by default for backward compatibility
    private float placeElapsed = 0f;
    private Vector3 placeStart;
    private Vector3 placeTarget;

    // cached colliders (may be Circle/Box or any Collider2D)
    private Collider2D[] _colliders;

    // ----------------- Helper: lazy init for colliders -----------------
    private void EnsureColliders()
    {
        if (_colliders != null) return;
        _colliders = GetComponentsInChildren<Collider2D>(true);
        if (_colliders == null)
            _colliders = new Collider2D[0];
    }

    protected virtual void Start()
    {
        // Ensure we have colliders reference
        EnsureColliders();

        // Start with colliders disabled until the fragment is placed
        foreach (var c in _colliders)
        {
            if (c == null) continue;
            c.enabled = false;    // keep disabled until placement finished
            c.isTrigger = true;   // fragments act as triggers/hazards
        }

        // Build masks and apply owner ignore if Start ran after InitializeFragment set owner
        Collider2D myCol = GetComponent<Collider2D>();
        if (myCol != null)
        {
            // re-enable cycle to ensure IgnoreCollision calls take effect (same pattern as Spell.Start)
            myCol.enabled = false;

            int exitMask = (int)exitTileInteraction;
            int allowedMask = 0;
            allowedMask |= (int)whatStopsMovement;
            allowedMask |= (int)enemyLayer;
            allowedMask |= (int)playerLayer;

            foreach (var otherCollider in FindObjectsOfType<Collider2D>())
            {
                if (otherCollider == null) continue;
                int otherLayerBit = 1 << otherCollider.gameObject.layer;
                if ((otherLayerBit & exitMask) != 0)
                {
                    Physics2D.IgnoreCollision(myCol, otherCollider, true);
                    continue;
                }
                if ((otherLayerBit & allowedMask) == 0)
                {
                    Physics2D.IgnoreCollision(myCol, otherCollider, true);
                }
            }
        }

        // Ensure start/target are consistent if Start runs before Initialize
        startPosition = transform.position;
        targetPosition = startPosition + (Vector3)(direction * currentSegmentLength);

        // Fragments are stationary hazards by default (movement handled only during placement)
        isMoving = false;
        moveTime = 0f;

        activeSpells.Add(this);
    }

    /// <summary>
    /// Call this after instantiation to set owner/lifetime/damage and ensure owner collisions are ignored.
    /// You may also call SetTargetPosition(...) to animate placement to a different tile.
    /// </summary>
    public void InitializeFragment(GameObject owner, int lifetimeTurns, int damage, bool destroyOnStep)
    {
        this.owner = owner;
        ApplyOwnerCollisionIgnore();

        this.lifetimeTurns = Mathf.Max(1, lifetimeTurns);
        this.damageAmountOnStep = damage;
        this.destroyOnStep = destroyOnStep;

        // ensure stationary by default
        isMoving = false;
    }

    /// <summary>
    /// Tell the fragment to move smoothly from its current location to `target` over `duration` seconds.
    /// Collider(s) will be disabled until placement completes.
    /// Safe to call before Start().
    /// </summary>
    public void SetTargetPosition(Vector3 target, float duration = -1f)
    {
        // ensure colliders array exists so DisableColliders doesn't NRE
        EnsureColliders();

        placeStart = transform.position;
        placeTarget = new Vector3(target.x, target.y, transform.position.z); // preserve z
        if (duration > 0f) placeDuration = duration;
        placeElapsed = 0f;
        placed = false;

        // disable any colliders while moving (safe even if they are already disabled)
        DisableColliders();
    }

    protected virtual void Update()
    {
        // If still placing, lerp position
        if (!placed)
        {
            placeElapsed += Time.deltaTime;
            float t = Mathf.Clamp01(placeElapsed / Mathf.Max(0.0001f, placeDuration));
            transform.position = Vector3.Lerp(placeStart, placeTarget, t);

            if (t >= 1f)
            {
                // placement finished
                placed = true;
                transform.position = placeTarget;
                EnableColliders();
            }
        }

        // Note: fragments do not use Spell.Update movement interpolation
    }

    protected override void MoveFurther()
    {
        // If fragment is placed but colliders are oddly still disabled, enable defensively
        if (placed)
            EnableColliders();

        turnsAlive++;
        if (turnsAlive >= lifetimeTurns)
            Destroy(gameObject);
    }

    private void EnableColliders()
    {
        EnsureColliders();
        foreach (var c in _colliders)
        {
            if (c == null) continue;
            c.enabled = true;
            c.isTrigger = true;
        }
    }

    private void DisableColliders()
    {
        EnsureColliders();
        foreach (var c in _colliders)
        {
            if (c == null) continue;
            c.enabled = false;
        }
    }

    protected override void OnTriggerEnter2D(Collider2D other)
    {
        // Defensive: ignore owner's colliders
        if (owner != null)
        {
            if (other.gameObject == owner || other.transform.IsChildOf(owner.transform))
                return;
        }

        // Fragments are ground hazards; do not treat walls as stepping
        if ((whatStopsMovement & (1 << other.gameObject.layer)) != 0)
        {
            return;
        }

        var damageable = other.GetComponent<IDamageable>();
        if (damageable != null)
        {
            damageable.TakeDamage(damageAmountOnStep);
            Debug.Log($"FractureFragment: Damaged {other.name} for {damageAmountOnStep}");

            if (destroyOnStep)
                Destroy(gameObject);
        }

        // don't call base.OnTriggerEnter2D (which would destroy)
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        // nothing extra needed, colliders/children will be cleaned by Unity
    }
}
