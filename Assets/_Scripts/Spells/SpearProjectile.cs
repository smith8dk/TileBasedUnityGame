using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// SpearProjectile: simple MonoBehaviour that travels to a target and deals damage on arrival or when a late trigger occurs.
/// - Call Launch(targetWorldPos, owner, travelDuration, damage)
/// - Requires a Collider2D (set isTrigger = true) and a Rigidbody2D (kinematic recommended) on the prefab to receive OnTriggerEnter2D when stationary.
/// Behavior:
///   * While flying: collider is disabled and OnTriggerEnter2D will early-return (no effect).
///   * On arrival: collider is enabled, an immediate overlap sweep is performed (damage + cleanup), then the projectile destroys itself.
///   * If something enters the collider after arrival, OnTriggerEnter2D will run (also ignores owner).
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class SpearProjectile : MonoBehaviour
{
    [Header("Flight")]
    public float defaultTravelDuration = 0.20f;

    [Header("Damage")]
    public int damageAmount = 40;
    public float arrivalDamageRadius = 0.25f;
    public LayerMask arrivalLayerMask = ~0; // default: everything

    [Header("Behavior")]
    [Tooltip("If true, the spear will rotate to face its travel direction.")]
    public bool rotateToDirection = false;

    // runtime
    private Vector3 _target;
    private GameObject _owner;
    private float _travelDuration = 0.2f;
    private bool _launched = false;
    private bool _arrived = false;
    private bool _isMoving = false;
    private Collider2D _collider;
    private HashSet<IDamageable> _damaged = new HashSet<IDamageable>();

    private void Awake()
    {
        _collider = GetComponent<Collider2D>();
        if (_collider == null)
            Debug.LogWarning("[SpearProjectile] No Collider2D found on spear prefab.");
    }

    /// <summary>
    /// Called when the projectile reaches its intended target position (end of flight).
    /// Performs an overlap sweep to damage IDamageable objects and destroys placeholder SpearFallSpell if present.
    /// </summary>
    private void OnArrivedAtTarget()
    {
        if (_arrived) return;
        _arrived = true;

        // enable collider so late triggers can occur (also useful if other objects enter after arrival)
        if (_collider != null)
            _collider.enabled = true;

        // perform immediate overlap sweep and deal damage
        Collider2D[] hits;
        if (arrivalLayerMask.value == 0)
            hits = Physics2D.OverlapCircleAll(transform.position, arrivalDamageRadius);
        else
            hits = Physics2D.OverlapCircleAll(transform.position, arrivalDamageRadius, arrivalLayerMask);

        bool anyHit = false;
        if (hits != null && hits.Length > 0)
        {
            foreach (var c in hits)
            {
                if (c == null) continue;

                // ignore owner
                if (_owner != null && (c.gameObject == _owner || c.transform.IsChildOf(_owner.transform)))
                    continue;

                // 1) damageable targets
                var dmg = c.GetComponent<IDamageable>();
                if (dmg != null && !_damaged.Contains(dmg))
                {
                    _damaged.Add(dmg);
                    try
                    {
                        dmg.TakeDamage(damageAmount);
                        Debug.Log($"[SpearProjectile] Dealt {damageAmount} damage to {c.gameObject.name} on arrival.");
                    }
                    catch (System.Exception ex)
                    {
                        Debug.LogWarning($"[SpearProjectile] Exception calling TakeDamage on {c.gameObject.name}: {ex.Message}");
                    }
                    anyHit = true;
                }

                // 2) placeholder target: SpearFallSpell — remove it so the visual disappears
                var placeholder = c.GetComponent<SpearFallSpell>() ?? c.GetComponentInParent<SpearFallSpell>();
                if (placeholder != null)
                {
                    try { Destroy(placeholder.gameObject); } catch { }
                    anyHit = true;
                }
            }
        }
        else
        {
            Debug.Log($"[SpearProjectile] Arrival sweep found no IDamageable targets at {transform.position}.");
        }
    }

    /// <summary>
    /// OnTriggerEnter2D handles collisions that happen AFTER arrival (when collider is enabled).
    /// While _isMoving==true we early-return so moving spear does not interact.
    /// </summary>
    private void OnTriggerEnter2D(Collider2D other)
    {
        if (other == null || other.gameObject == null) return;

        // ignore while moving
        if (_isMoving) return;

        // ignore owner
        if (_owner != null && (other.gameObject == _owner || other.transform.IsChildOf(_owner.transform)))
            return;

        // Damageable hit
        var dmg = other.GetComponent<IDamageable>();
        if (dmg != null && !_damaged.Contains(dmg))
        {
            _damaged.Add(dmg);
            try
            {
                dmg.TakeDamage(damageAmount);
                Debug.Log($"[SpearProjectile] Dealt {damageAmount} damage to {other.gameObject.name} on trigger.");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[SpearProjectile] Exception calling TakeDamage on {other.gameObject.name}: {ex.Message}");
            }
            return;
        }
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.25f);
        Gizmos.DrawSphere(transform.position, arrivalDamageRadius);
    }
}
