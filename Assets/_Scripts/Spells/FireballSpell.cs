using System.Collections;
using UnityEngine;

public class FireballSpell : Spell
{
    [Header("Explosion Settings")]
    [Tooltip("Animator to control the fireball/explosion animations")]
    [SerializeField] private Animator animator;
    [Tooltip("Name of the trigger parameter in the Animator for the explosion")]
    [SerializeField] private string explodeTrigger = "Explode";
    [Tooltip("How long to wait (seconds) for the explosion animation before destroying")]
    [SerializeField] private float explosionDuration = 0.5f;

    private Collider2D _collider2D;
    private bool _hasExploded = false;

    protected override void Start()
    {
        base.Start();
        _collider2D = GetComponent<Collider2D>();
        if (animator == null)
            animator = GetComponentInChildren<Animator>();
    }

    /// <summary>
    /// Override Initialize so we can rotate the fireball to face its movement direction
    /// before kicking off the base logic. Also accept the owner and pass it to the base.
    /// </summary>
    public override void Initialize(Vector3 dir, GameObject owner = null, GameObject sourceDraggable = null, int cooldownTurns = 0)
    {
        // Compute facing direction. If dir is near-zero, fall back to cardinal direction snap.
        Vector3 facingDir;
        if (dir.sqrMagnitude > 1e-6f)
            facingDir = dir.normalized;
        else
            facingDir = SnapToCardinalDirection(GlobalDirection.Direction);

        float angleDeg = Mathf.Atan2(facingDir.y, facingDir.x) * Mathf.Rad2Deg;

        // If your sprite faces "up" by default, subtract 90 degrees. Adjust if needed.
        transform.rotation = Quaternion.Euler(0f, 0f, angleDeg - 90f);

        // Now call base to set owner, start movement, and apply owner-collision ignores.
        base.Initialize(dir, owner);
    }

    protected override void OnCollisionEnter2D(Collision2D collision)
    {
        // Defensive: ignore collisions with the owner (if somehow they occur)
        if (owner != null)
        {
            if (collision.gameObject == owner || collision.transform.IsChildOf(owner.transform))
                return;
        }

        // only react once
        if (_hasExploded)
            return;

        // check if it hit something that stops movement
        if ((whatStopsMovement & (1 << collision.gameObject.layer)) != 0)
        {
            Explode();
        }
    }

    /// <summary>
    /// Called when the trigger collider hits another object
    /// </summary>
    protected override void OnTriggerEnter2D(Collider2D other)
    {
        // Defensive: ignore owner's colliders (if somehow OnTrigger fires)
        if (owner != null)
        {
            if (other.gameObject == owner || other.transform.IsChildOf(owner.transform))
                return;
        }

        if (_hasExploded)
            return;

        _hasExploded = true;
        baseSpeed = 0f;

        // Deal damage if applicable (only to non-owner targets)
        var damageable = other.GetComponent<IDamageable>();
        if (damageable != null)
        {
            damageable.TakeDamage(damageAmount);
            Debug.Log("Hit for " + damageAmount);
        }

        // Disable collider so no further interactions happen
        if (_collider2D == null) _collider2D = GetComponent<Collider2D>();
        if (_collider2D != null)
            _collider2D.enabled = false;

        // Trigger the explosion animation
        if (animator != null && !string.IsNullOrEmpty(explodeTrigger))
        {
            animator.SetTrigger(explodeTrigger);
            Debug.Log("Explosion triggered.");
        }

        // Wait for the animation, then destroy the fireball
        StartCoroutine(WaitAndDestroy());
    }

    private IEnumerator WaitAndDestroy()
    {
        // wait for the animation to play
        yield return new WaitForSeconds(explosionDuration);

        Destroy(gameObject);
    }

    /// <summary>
    /// Shared explosion path (used by collision handler).
    /// </summary>
    private void Explode()
    {
        if (_hasExploded) return;
        _hasExploded = true;

        // stop further movement
        isMoving = false;
        baseSpeed = 0f;

        // disable collider so no further collisions
        if (_collider2D == null) _collider2D = GetComponent<Collider2D>();
        if (_collider2D != null)
            _collider2D.enabled = false;

        // trigger the explosion animation
        if (animator != null && !string.IsNullOrEmpty(explodeTrigger))
        {
            animator.SetTrigger(explodeTrigger);
            Debug.Log("Explosion triggered.");
        }

        // schedule actual destruction after the explosion plays
        StartCoroutine(WaitAndDestroy());
    }
}
