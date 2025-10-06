using UnityEngine;

/// <summary>
/// SwordAttack: spawns a short-lived hurtbox that damages IDamageable targets.
/// - External code must call Initialize(dir, owner) so owner & direction are known.
/// - Calls ApplyOwnerCollisionIgnore() so the spell's collider ignores the owner's colliders (same logic as Spell.cs).
/// - Also ignores hurtbox <-> owner collisions for any colliders on the hurtbox instance.
/// - Overrides MoveFurther to no-op and destroys itself after hurtboxDuration.
/// - Rotates hurtbox according to the direction passed to Initialize.
/// - If the hurtbox collides with an object tagged "Spell", sets a static flag and logs a debug message.
/// </summary>
public class SwordAttack : Spell
{
    [Header("Hurtbox")]
    public GameObject hurtboxPrefab;              // optional prefab (should contain Collider2D(s) set as trigger)
    public Vector2 hurtboxSize = new Vector2(0.6f, 0.6f);
    public float hurtboxDuration = 0.15f;         // how long the spawned hurtbox lives (and how long the spell stays alive)
    public Vector3 localOffset = new Vector3(1f, 0f, 0f); // offset relative to the spell's position before rotation

    private GameObject hurtboxInstance;

    // Turn-skip flags (checked/consumed by PlayerController per turn)
    public static bool SpellCollisionThisTurn = false;
    public static bool SpellCollisionConsumedThisTurn = false;

    protected override void Start()
    {
        base.Start();
        // External code should call Initialize(dir, owner) before the hurtbox spawns.
        // Keep spell non-moving by default.
        isMoving = false;
    }

    /// <summary>
    /// External scripts must call Initialize(dir, owner).
    /// Initialize sets owner/direction, ignores owner collisions, spawns the hurtbox, and schedules destruction.
    /// </summary>
    public override void Initialize(Vector3 dir, GameObject owner = null, GameObject sourceDraggable = null, int cooldownTurns = 0)
    {
        // set owner first so ApplyOwnerCollisionIgnore can ignore owner colliders
        this.owner = owner;

        // ignore collisions between this spell's collider(s) and the owner's colliders
        ApplyOwnerCollisionIgnore();

        // set direction (use snap fallback)
        if (dir.sqrMagnitude > 1e-6f)
            direction = dir.normalized;
        else
            direction = SnapToCardinalDirection(GlobalDirection.Direction);

        // This attack does not use segment movement
        isMoving = false;

        // Spawn and configure the hurtbox now that owner/direction are known
        SpawnHurtbox();

        // destroy this spell object after the activation period
        Destroy(this.gameObject, hurtboxDuration);
    }

    // Prevent Spell.StepAllSpells from moving this spell
    protected override void MoveFurther()
    {
        // intentionally empty
    }

    private void SpawnHurtbox()
    {
        // compute rotation from direction (assumes prefab faces +X)
        float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
        Vector3 rotatedOffset = Quaternion.Euler(0f, 0f, angle) * localOffset;
        Vector3 worldPos = transform.position + rotatedOffset;
        Quaternion rot = Quaternion.Euler(0f, 0f, angle);

        if (hurtboxPrefab != null)
        {
            hurtboxInstance = Instantiate(hurtboxPrefab, worldPos, rot);
            // parent while keeping world transform
            hurtboxInstance.transform.SetParent(transform, true);

            // Ensure all Collider2D components are triggers and have a HurtBoxDamage attached
            foreach (var col in hurtboxInstance.GetComponentsInChildren<Collider2D>())
            {
                if (col == null) continue;
                col.isTrigger = true;

                // ensure a HurtBoxDamage exists on the same GameObject as the collider so OnTriggerEnter2D fires
                var hb = col.gameObject.GetComponent<HurtBoxDamage>();
                if (hb == null) hb = col.gameObject.AddComponent<HurtBoxDamage>();
                hb.damage = damageAmount;
                hb.owner = owner;
            }
        }
        else
        {
            // runtime hurtbox: single BoxCollider2D on the root
            hurtboxInstance = new GameObject("Hurtbox");
            hurtboxInstance.transform.position = worldPos;
            hurtboxInstance.transform.rotation = rot;
            hurtboxInstance.transform.SetParent(transform, true);

            var box = hurtboxInstance.AddComponent<BoxCollider2D>();
            box.isTrigger = true;
            box.size = hurtboxSize;

            var rb = hurtboxInstance.AddComponent<Rigidbody2D>();
            rb.isKinematic = true;

            // attach HurtBoxDamage on same GameObject as the collider
            var hb = hurtboxInstance.AddComponent<HurtBoxDamage>();
            hb.damage = damageAmount;
            hb.owner = owner;
        }

        // Ensure hurtbox colliders ignore owner's colliders (mirror of ApplyOwnerCollisionIgnore but for hurtbox)
        IgnoreHurtboxOwnerCollisions();

        // Auto-destroy hurtbox after duration (redundant if parent is destroyed, but safe)
        Destroy(hurtboxInstance, hurtboxDuration);
    }

    /// <summary>
    /// Ensure every Collider2D on the hurtbox ignores every Collider2D on the owner (if any).
    /// </summary>
    private void IgnoreHurtboxOwnerCollisions()
    {
        if (hurtboxInstance == null || owner == null) return;

        var hurtboxCols = hurtboxInstance.GetComponentsInChildren<Collider2D>();
        var ownerCols = owner.GetComponentsInChildren<Collider2D>();

        if (hurtboxCols == null || ownerCols == null) return;

        foreach (var h in hurtboxCols)
        {
            if (h == null) continue;
            foreach (var o in ownerCols)
            {
                if (o == null) continue;
                Physics2D.IgnoreCollision(h, o, true);
            }
        }
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (hurtboxInstance != null)
            Destroy(hurtboxInstance);
    }

    /// <summary>
    /// Component added to the same GameObject as each hurtbox Collider2D so trigger events are received reliably.
    /// It handles damage and the "Spell" tag debug logging and sets the global turn-skip flag.
    /// </summary>
    public class HurtBoxDamage : MonoBehaviour
    {
        public int damage = 5;
        public GameObject owner;

        private void OnTriggerEnter2D(Collider2D other)
        {
            // If other is the owner (or child of owner), ignore
            if (owner != null && (other.gameObject == owner || other.transform.IsChildOf(owner.transform)))
                return;

            // Debug: if the other is tagged "Spell", log a message and mark the flag for player's turn logic
            if (other.CompareTag("Spell"))
            {
                Debug.Log($"Hurtbox hit object with tag 'Spell': {other.gameObject.name}");
                // mark that a Spell collision occurred this player-turn
                SwordAttack.SpellCollisionThisTurn = true;
            }

            var dmg = other.GetComponent<IDamageable>();
            if (dmg != null)
            {
                dmg.TakeDamage(damage);
                // Note: if you want the spell/hurtbox to be destroyed immediately on first hit, you can:
                // Destroy(transform.root.gameObject);
            }
        }
    }
}
