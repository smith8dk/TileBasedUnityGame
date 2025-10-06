using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class WindSpell : Spell
{
    private Collider2D boxCollider;

    private void Awake()
    {
        // Cache and configure the collider as a trigger, but start disabled
        boxCollider = GetComponent<Collider2D>();
        boxCollider.isTrigger = true;
        boxCollider.enabled = false;

        // Immediately snap to the player’s current position
        var playerGO = GameObject.FindWithTag("Player");
        if (playerGO != null)
            transform.position = playerGO.transform.position;
        else
            Debug.LogWarning("[WindSpell] No GameObject tagged 'Player' found.");
    }

    protected override void Start()
    {
        // Lock movement at that position
        isMoving = false;
        startPosition = transform.position;
        targetPosition = transform.position;
        moveTime = 1f;

        base.Start();
    }

    // signature matches Spell.Initialize(owner-aware)
    public override void Initialize(Vector3 dir, GameObject owner = null, GameObject sourceDraggable = null, int cooldownTurns = 0)
    {
        // Snap again in case player moved before Initialize
        var playerGO = GameObject.FindWithTag("Player");
        if (playerGO != null)
            transform.position = playerGO.transform.position;

        // Call base to record owner and apply owner collision ignores
        base.Initialize(dir, owner);

        // Lock in place
        isMoving = false;
        startPosition = transform.position;
        targetPosition = transform.position;
        moveTime = 0f;
    }

    /// <summary>
    /// Called by an Animation Event on the first frame you want damage to register.
    /// </summary>
    public void EnableboxCollider()
    {
        boxCollider.enabled = true;
        // Re-apply owner-ignore defensively after enabling the collider
        ApplyOwnerCollisionIgnore();
    }

    /// <summary>
    /// Called by an Animation Event on the last frame you want damage to register.
    /// </summary>
    public void DisableboxCollider()
    {
        boxCollider.enabled = false;
    }

    public void DestroySelf()
    {
        Destroy(gameObject);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // Defensive: ignore owner's colliders (if somehow OnTrigger fires)
        if (owner != null)
        {
            if (other.gameObject == owner || other.transform.IsChildOf(owner.transform))
                return;
        }

        // Apply damage if possible
        var damageable = other.GetComponent<IDamageable>();
        if (damageable != null)
        {
            damageable.TakeDamage(damageAmount);
        }

        // Only if this was an EnemyController (or any class with HandleKnockback) do we knock it back
        if (other.TryGetComponent<EnemyController>(out var enemy))
        {
            enemy.HandleKnockback();
        }
    }
}
