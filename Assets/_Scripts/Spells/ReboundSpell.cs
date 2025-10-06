using System.Collections;
using UnityEngine;

/// <summary>
/// ReboundSpell:
/// - Continuous movement with circlecast-based bounces (no movePoint / lerp).
/// - Pauses whenever cumulative distance traveled is an exact multiple of 4.0 units (tiles).
/// - MoveFurther() resumes motion until next multiple-of-4 is reached.
/// - Speed increases multiplicatively and damage additively on each bounce.
/// </summary>
[RequireComponent(typeof(Collider2D))]
public class ReboundSpell : Spell
{
    [Header("Lifetime / Bounce")]
    [SerializeField] private int maxBounces = 10;
    [SerializeField] private float maxLifetimeSeconds = 0f; // <=0 disables

    [Header("Per-bounce scaling")]
    [SerializeField] private float speedMultiplierPerBounce = 1.25f;
    [SerializeField] private int damageIncreasePerBounce = 1;

    [Header("Movement / Collision tuning")]
    [Tooltip("Radius used for circlecasts and overlap checks.")]
    [SerializeField] private float collisionRadius = 0.18f;
    [Tooltip("Distance to nudge away from surface after bounce.")]
    [SerializeField] private float nudgeDistance = 0.03f;
    [Tooltip("Max reflections handled in a single frame to avoid infinite loops.")]
    [SerializeField] private int maxReflectionsPerFrame = 4;

    // runtime state
    private Vector2 _velocityDirection = Vector2.right;
    private float _nominalSpeed = 5f;
    private float _lifeTimer = 0f;
    private int _bounceCount = 0;

    // pause / step tracking
    private float _cumulativeDistance = 0f;
    private float _distanceUntilPause = 4f;
    private bool _isPaused = true;

    // cache
    private Collider2D _collider2d;
    private const float EPS = 1e-5f;

    protected override void Start()
    {
        base.Start();
        _collider2d = GetComponent<Collider2D>();

        if (_collider2d != null && collisionRadius <= 0f)
        {
            collisionRadius = Mathf.Max(0.01f, Mathf.Max(_collider2d.bounds.extents.x, _collider2d.bounds.extents.y) * 0.5f);
        }

        _nominalSpeed = Mathf.Max(0.0001f, baseSpeed);
    }

    // NOTE: signature matches base.Initialize(owner-aware)
    public override void Initialize(Vector3 dir, GameObject owner = null, GameObject sourceDraggable = null, int cooldownTurns = 0)
    {
        // Call base first so owner is recorded and owner-collision ignores are applied.
        base.Initialize(dir, owner);

        // Ensure we have a non-zero direction
        if (dir.sqrMagnitude < EPS) dir = Vector3.right;
        _velocityDirection = SnapToEightWay(new Vector2(dir.x, dir.y)).normalized;

        float angleDeg = Mathf.Atan2(_velocityDirection.y, _velocityDirection.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, angleDeg - 90f);

        _lifeTimer = 0f;
        _bounceCount = 0;
        _nominalSpeed = Mathf.Max(0.0001f, baseSpeed);
        _cumulativeDistance = 0f;

        _distanceUntilPause = 4f - Mathf.Repeat(_cumulativeDistance, 4f);
        if (_distanceUntilPause <= EPS) _distanceUntilPause = 4f;

        _isPaused = false;
        isMoving = true; // moving immediately after spawn
    }

    protected override void Update()
    {
        // keep isMoving in sync with paused state
        isMoving = !_isPaused;

        if (maxLifetimeSeconds > 0f)
        {
            _lifeTimer += Time.deltaTime;
            if (_lifeTimer >= maxLifetimeSeconds)
            {
                Destroy(gameObject);
                return;
            }
        }

        if (_isPaused || _nominalSpeed <= 0.0001f) return;

        float remainingFrameDistance = Mathf.Min(_nominalSpeed * Time.deltaTime, _distanceUntilPause);
        int reflections = 0;
        Vector2 position = (Vector2)transform.position;

        while (remainingFrameDistance > EPS && reflections < maxReflectionsPerFrame)
        {
            RaycastHit2D hit = Physics2D.CircleCast(position, collisionRadius, _velocityDirection, remainingFrameDistance + 0.001f, whatStopsMovement);

            if (hit.collider == null)
            {
                position += _velocityDirection * remainingFrameDistance;
                _cumulativeDistance += remainingFrameDistance;
                _distanceUntilPause -= remainingFrameDistance;
                remainingFrameDistance = 0f;
                break;
            }
            else
            {
                float travelToHit = Mathf.Max(0f, hit.distance - 0.001f);
                if (travelToHit > EPS)
                {
                    position += _velocityDirection * travelToHit;
                    _cumulativeDistance += travelToHit;
                    _distanceUntilPause -= travelToHit;
                    remainingFrameDistance -= travelToHit;
                }

                // Damage targets at the contact position (ignoring owner)
                TryDamageAtPosition(position);

                Vector2 reflected = Vector2.Reflect(_velocityDirection, hit.normal).normalized;
                Vector2 snapped = SnapToEightWay(reflected);
                if (snapped.sqrMagnitude > EPS) reflected = snapped;

                _bounceCount++;
                _nominalSpeed *= Mathf.Max(0.0001f, speedMultiplierPerBounce);
                damageAmount += damageIncreasePerBounce;

                position += hit.normal * nudgeDistance;
                _velocityDirection = reflected.normalized;

                if (_bounceCount >= maxBounces)
                {
                    Destroy(gameObject);
                    return;
                }

                reflections++;

                if (_distanceUntilPause <= EPS)
                {
                    remainingFrameDistance = 0f;
                    break;
                }

                float allowedNow = Mathf.Min(remainingFrameDistance, _distanceUntilPause);
                if (allowedNow <= EPS) break;

                continue;
            }
        }

        transform.position = position;

        // final check at the new position
        TryDamageAtPosition(position);

        if (_distanceUntilPause <= EPS)
        {
            _cumulativeDistance = Mathf.Round(_cumulativeDistance / 4f) * 4f;
            _distanceUntilPause = 0f;
            _isPaused = true;
            isMoving = false; // stopped here
        }
    }

    protected override void MoveFurther()
    {
        float rem = Mathf.Repeat(_cumulativeDistance, 4f);
        if (rem <= EPS) _distanceUntilPause = 4f;
        else _distanceUntilPause = 4f - rem;
        if (_distanceUntilPause <= EPS) _distanceUntilPause = 4f;

        _isPaused = false;
        isMoving = true;
    }

    private void TryDamageAtPosition(Vector2 worldPos)
    {
        // Full overlap checks; ignore self-collider and owner colliders
        Collider2D[] hits = Physics2D.OverlapCircleAll(worldPos, collisionRadius, ~0);
        foreach (var c in hits)
        {
            if (c == _collider2d) continue;

            // Defensive: ignore owner's colliders (owner defined in base Spell)
            if (owner != null)
            {
                if (c.gameObject == owner || c.transform.IsChildOf(owner.transform))
                    continue;
            }

            var damageable = c.GetComponent<IDamageable>();
            if (damageable != null)
            {
                damageable.TakeDamage(damageAmount);
                Destroy(gameObject);
                return;
            }
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
            Destroy(gameObject);
            return;
        }
    }

    private Vector2 SnapToEightWay(Vector2 v)
    {
        if (v.sqrMagnitude < EPS) return Vector2.right;
        float angle = Mathf.Atan2(v.y, v.x) * Mathf.Rad2Deg;
        float snappedAngle = Mathf.Round(angle / 45f) * 45f;
        float r = snappedAngle * Mathf.Deg2Rad;
        return new Vector2(Mathf.Cos(r), Mathf.Sin(r)).normalized;
    }

    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawLine(transform.position, (Vector3)transform.position + (Vector3)_velocityDirection * 0.5f);
        Gizmos.color = Color.yellow;
        Gizmos.DrawWireSphere(transform.position, collisionRadius);
    }
}
