using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class CorkscrewSpell : Spell
{
    [Header("Helix Settings")]
    [Tooltip("How tightly the spell spirals (radians per unit traveled)")]
    [SerializeField] private float spiralFrequency = 10f;
    [Tooltip("Radius of the helix curve")]
    [SerializeField] private float spiralRadius = 0.5f;

    [Header("Multi-Helix")]
    [Tooltip("Phase offset in radians (0..2π)")]
    [SerializeField] private float phaseOffset = 0f;
    [Tooltip("Prefab to clone for the second strand")]
    [SerializeField] private CorkscrewSpell spellPrefab;

    [Header("Trail")]
    [Tooltip("Assign your TrailRenderer here")]
    [SerializeField] private TrailRenderer trailRenderer;

    // Track trail timing
    private float _maxTrailTime;
    private float _remainingTrailTime;
    // Detect movement stop
    private bool  _wasMoving = false;

    protected override void Start()
    {
        base.Start();

        if (trailRenderer != null)
        {
            _maxTrailTime       = trailRenderer.time;
            _remainingTrailTime = _maxTrailTime;
        }
    }

    /// <summary>
    /// Make Initialize owner-aware and ensure twin gets owner forwarded.
    /// </summary>
    public override void Initialize(Vector3 dir, GameObject owner = null, GameObject sourceDraggable = null, int cooldownTurns = 0)
    {
        // Call base first so owner is recorded and owner-collider ignores are applied
        base.Initialize(dir, owner);

        // Spawn the secondary strand if this is the primary (phaseOffset == 0)
        if (phaseOffset == 0f && spellPrefab != null)
        {
            var twin = Instantiate(spellPrefab, transform.position, transform.rotation);

            // Give the twin the opposite phase and start it moving in the same direction.
            twin.phaseOffset = Mathf.PI;

            // Initialize the twin with the same direction and owner so it behaves identically and ignores owner collisions
            twin.Initialize(dir, owner);
        }
    }

    protected override void MoveFurther()
    {
        // Resume trail emission and restore remaining fade time
        if (trailRenderer != null)
        {
            trailRenderer.emitting = true;
            trailRenderer.time     = _remainingTrailTime;
        }

        // Standard 3-tile step
        Vector3 baseStart  = transform.position;
        Vector3 baseTarget = baseStart + (Vector3)(direction * 3f);

        startPosition  = baseStart;
        targetPosition = baseTarget;
        isMoving       = true;
        moveTime       = 0f;
    }

    protected override void Update()
    {
        base.Update();

        // Apply helix offset while moving
        if (isMoving)
        {
            float t   = Mathf.Clamp01(moveTime);
            Vector2 perp = new Vector2(-direction.y, direction.x);
            float offset = Mathf.Sin(t * spiralFrequency + phaseOffset) * spiralRadius;
            transform.position += (Vector3)(perp * offset);

            // Decrement trail lifetime
            if (trailRenderer != null)
            {
                _remainingTrailTime -= Time.deltaTime;
                _remainingTrailTime = Mathf.Max(_remainingTrailTime, 0f);
                trailRenderer.time  = _remainingTrailTime;
            }
        }

        // Pause trail when movement stops
        if (_wasMoving && !isMoving && trailRenderer != null)
        {
            trailRenderer.emitting = false;
        }

        _wasMoving = isMoving;
    }

    protected override void OnCollisionEnter2D(Collision2D collision)
    {
        // Defensive: ignore collisions with the owner (in case owner-collision-ignore hasn't been applied yet)
        if (owner != null)
        {
            if (collision.gameObject == owner || collision.transform.IsChildOf(owner.transform))
                return;
        }

        if ((whatStopsMovement & (1 << collision.gameObject.layer)) != 0)
        {
            // Detach the trail so it can finish fading out
            if (trailRenderer != null)
            {
                trailRenderer.emitting = false;
                trailRenderer.transform.SetParent(null, true);
                Destroy(trailRenderer.gameObject, _remainingTrailTime);
            }

            Destroy(gameObject);
        }
    }
}
