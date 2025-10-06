using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Base Spell class with per-segment length support so variable-length steps (e.g. leftover distance after bounce)
/// move at the expected world-space speed.
/// </summary>
public class Spell : MonoBehaviour
{
    public float baseSpeed = 5f; // Base speed of the projectile in tiles per second
    protected float currentSpeed; // Current speed
    protected Vector2 direction = Vector2.right; // Movement direction (unit vector)

    [SerializeField] protected LayerMask whatStopsMovement;
    [SerializeField] protected LayerMask exitTileInteraction;
    [SerializeField] protected LayerMask enemyLayer;
    [SerializeField] protected LayerMask playerLayer; // allow specifying Player layer in inspector

    protected GameObject owner; // who spawned this spell (set at Instantiate/Initialize)

    protected Transform movePoint;
    protected Vector3 startPosition;
    protected Vector3 targetPosition;
    protected bool isMoving = false;
    protected float moveTime;

    // length (in world units) of the *current* segment being traversed.
    // default 3f (previous hard-coded step)
    protected float currentSegmentLength = 3f;

    [SerializeField] public int damageAmount = 5;

    // Static list to keep track of all active spells
    protected static List<Spell> activeSpells = new List<Spell>();

    protected virtual void Start()
    {
        // DON'T overwrite isMoving here — Initialize(...) may have already set it.
        // Just perform setup that doesn't clobber runtime state.

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

        // Create movePoint GameObject (kept for compatibility)
        if (movePoint == null)
        {
            movePoint = new GameObject("SpellMovepoint").transform;
            movePoint.position = transform.position;
        }

        // Ensure start/target are consistent if Start runs before Initialize
        startPosition = transform.position;
        targetPosition = startPosition + (Vector3)(direction * currentSegmentLength);

        // leave isMoving as-is (Initialize will normally set it true)
        moveTime = 0;
        currentSpeed = baseSpeed;

        activeSpells.Add(this);
    }

    protected virtual void OnDestroy()
    {
        activeSpells.Remove(this);
        if (movePoint != null)
        {
            Destroy(movePoint.gameObject);
        }
    }

    protected virtual void Update()
    {
        currentSpeed = baseSpeed;

        if (isMoving)
        {
            // Progress per second depends on currentSegmentLength so time-to-complete = currentSegmentLength / currentSpeed
            // moveTime is normalized 0..1
            moveTime += Time.deltaTime * (currentSpeed / Mathf.Max(0.0001f, currentSegmentLength));
            transform.position = Vector3.Lerp(startPosition, targetPosition, moveTime);
            if (moveTime >= 1f)
            {
                isMoving = false;
                transform.position = targetPosition;
            }
        }
    }

    protected virtual void OnCollisionEnter2D(Collision2D collision)
    {
        // If this collided with something in whatStopsMovement, destroy.
        if ((whatStopsMovement & (1 << collision.gameObject.layer)) != 0)
        {
            Destroy(gameObject);
        }
    }

    // Static method to move all active spells (used by your turn stepping)
    protected static void MoveSpells()
    {
        foreach (var spell in new List<Spell>(activeSpells))
        {
            if (spell != null)
            {
                spell.MoveFurther();
            }
        }
    }

    /// <summary>
    /// Default movement step: advance one segment in the current direction.
    /// Child classes can override to change stepping.
    /// </summary>
    protected virtual void MoveFurther()
    {
        startPosition = transform.position;
        currentSegmentLength = 3f; // default step size for a normal step
        targetPosition = startPosition + (Vector3)(direction.normalized * currentSegmentLength);
        if (movePoint != null)
            movePoint.position = targetPosition;
        isMoving = true;
        moveTime = 0;
    }

    public static bool AnySpellsActive() => activeSpells.Count > 0;

    public static bool AnySpellsMoving()
    {
        foreach (var s in activeSpells)
            if (s != null && s.isMoving)
                return true;
        return false;
    }

    protected Vector2 SnapToCardinalDirection(Vector3 dir)
    {
        if (dir.sqrMagnitude < 1e-6f)
            return Vector2.right;

        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        if (angle > -45f && angle <= 45f)
            return Vector2.right;
        else if (angle > 45f && angle <= 135f)
            return Vector2.up;
        else if (angle <= -45f && angle > -135f)
            return Vector2.down;
        else
            return Vector2.left;
    }

    /// <summary>
    /// Initialize the spell's direction and start movement.
    /// If dir is near-zero, fall back to 4-way snap.
    /// Owner is optional: pass the spawner so the spell won't hit its creator.
    /// sourceDraggable (optional): the draggable UI object that requested this spell; used to start cooldown.
    /// cooldownTurns (optional): how many turns to put the source draggable on cooldown when this spell spawns.
    /// </summary>
    public virtual void Initialize(Vector3 dir, GameObject owner = null, GameObject sourceDraggable = null, int cooldownTurns = 0)
    {
        // set owner first so Start/ApplyOwnerCollisionIgnore won't hit owner
        this.owner = owner;

        // make sure to ignore owner collisions even if Start has already run
        ApplyOwnerCollisionIgnore();

        if (dir.sqrMagnitude > 1e-6f)
        {
            direction = dir.normalized;
        }
        else
        {
            direction = SnapToCardinalDirection(GlobalDirection.Direction);
        }

        startPosition = transform.position;
        currentSegmentLength = 3f;
        targetPosition = startPosition + (Vector3)(direction.normalized * currentSegmentLength);

        if (movePoint != null)
            movePoint.position = targetPosition;

        isMoving = true;
        moveTime = 0;

        // // START cooldown for source draggable (only when the spell actually initializes/spawns)
        // if (sourceDraggable != null && cooldownTurns > 0 && CooldownManager.Instance != null)
        // {
        //     CooldownManager.Instance.StartCooldownForDraggable(sourceDraggable, cooldownTurns);
        // }
    }

    /// <summary>
    /// Ignore collisions between this spell's collider and every collider on the owner GameObject (and its children).
    /// Safe to call multiple times.
    /// </summary>
    protected void ApplyOwnerCollisionIgnore()
    {
        if (owner == null) return;

        Collider2D myCol = GetComponent<Collider2D>();
        if (myCol == null) return;

        foreach (var ownerCol in owner.GetComponentsInChildren<Collider2D>())
        {
            if (ownerCol != null)
                Physics2D.IgnoreCollision(myCol, ownerCol, true);
        }
    }

    protected virtual void OnTriggerEnter2D(Collider2D other)
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

        Destroy(gameObject);
    }

    public static void StepAllSpells()
    {
        foreach (var s in new List<Spell>(activeSpells))
            s.MoveFurther();
    }

    public static IEnumerable<Spell> GetActiveSpells() => activeSpells;
}
