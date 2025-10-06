using System.Collections;
using UnityEngine;

/// <summary>
/// ChakramSpell:
/// - Initialize() places the chakram 1 tile away from the owner in the given direction (spawn adjacent).
/// - Chakram then optionally travels with a curved path to the orbit radius, then orbits the center.
/// - Movement is performed by coroutines (SmoothMoveTo). The base Spell.Update() movement is disabled
///   by overriding Update() to avoid conflicts/teleporting.
/// - isMoving is set true while a smooth movement is in progress and false when finished.
/// - OnTriggerEnter2D deals damage to IDamageable (ignores owner).
/// - Destroys itself after completing a certain number of full rotations.
/// </summary>
public class ChakramSpell : Spell
{
    [Header("Orbit geometry")]
    [Tooltip("World distance representing a single tile.")]
    [SerializeField] private float cellSize = 1f;

    [Tooltip("Radius of the orbit in world units. Will be clamped to at least one tile (cellSize).")]
    [SerializeField] private float orbitRadius = 2f;

    [Header("Motion")]
    [Tooltip("Degrees the chakram advances each MoveFurther() call.")]
    [SerializeField] private float degreesPerStep = 45f;

    [Tooltip("Seconds used to smoothly interpolate between positions on each MoveFurther() step.")]
    [SerializeField] private float smoothStepDuration = 0.15f;

    [Tooltip("Duration used to smoothly move from the spawn adjacent tile to the orbit path.")]
    [SerializeField] private float initialTravelDuration = 0.18f;

    [Tooltip("How strong the outward curve is when traveling to the orbit path (0 = straight line).")]
    [SerializeField, Range(0f, 2f)] private float initialCurveStrength = 0.6f;

    [Tooltip("If true the chakram orbits clockwise; otherwise counter-clockwise.")]
    [SerializeField] private bool orbitClockwise = true;

    [Tooltip("If true the chakram rotates to face the tangent of motion.")]
    [SerializeField] private bool rotateToTangent = true;

    [Header("Lifetime")]
    [Tooltip("Maximum MoveFurther steps before the chakram destroys itself. 0 = infinite.")]
    [SerializeField] private int maxSteps = 0;

    [Tooltip("Number of full rotations before the chakram destroys itself. 0 = infinite.")]
    [SerializeField] private int maxRotations = 0;

    // internal state
    private Vector3 orbitCenter;      // fixed center captured at Initialize
    private float currentAngleDeg;    // current polar angle (degrees) measured from +X
    private bool isOrbiting = false;
    private int stepsTaken = 0;

    // rotation tracking
    private float totalRotationProgress = 0f; // in degrees

    // orbitStepSign: +1 => angle increases (CCW). -1 => angle decreases (CW).
    private float orbitStepSign = -1f;

    // coroutine handle
    private Coroutine smoothMoveCoroutine = null;

    protected override void Start()
    {
        base.Start();
        isMoving = false;
        baseSpeed = 0f;
        currentSpeed = 0f;
    }

    protected override void Update()
    {
        // Disable Spell.Update() movement
    }

    public override void Initialize(Vector3 dir, GameObject owner = null, GameObject sourceDraggable = null, int cooldownTurns = 0)
    {
        this.owner = owner;
        ApplyOwnerCollisionIgnore();

        // reset Spell state
        direction = Vector2.zero;
        startPosition = transform.position;
        currentSegmentLength = 0f;
        targetPosition = startPosition;
        if (movePoint != null) movePoint.position = transform.position;

        isMoving = false;
        moveTime = 0f;
        baseSpeed = 0f;
        currentSpeed = 0f;

        // capture center and radius
        orbitCenter = (owner != null) ? owner.transform.position : transform.position;
        orbitRadius = Mathf.Max(cellSize, orbitRadius);

        // orbit sign
        orbitStepSign = orbitClockwise ? -1f : 1f;

        // direction handling
        Vector2 dirVec;
        if (dir.sqrMagnitude > 1e-6f)
            dirVec = dir.normalized;
        else
            dirVec = SnapToCardinalDirection(GlobalDirection.Direction);

        // spawn adjacent
        Vector3 spawnPos = orbitCenter + new Vector3(dirVec.x * cellSize, dirVec.y * cellSize, 0f);
        transform.position = spawnPos;

        // initial angle
        currentAngleDeg = Mathf.Atan2(transform.position.y - orbitCenter.y, transform.position.x - orbitCenter.x) * Mathf.Rad2Deg;

        // desired orbit pos
        Vector3 desiredOrbitPos = orbitCenter +
            new Vector3(Mathf.Cos(currentAngleDeg * Mathf.Deg2Rad), Mathf.Sin(currentAngleDeg * Mathf.Deg2Rad), 0f) * orbitRadius;

        // travel out
        if (Vector3.Distance(transform.position, desiredOrbitPos) > 1e-4f)
        {
            isMoving = true;
            if (smoothMoveCoroutine != null) StopCoroutine(smoothMoveCoroutine);
            smoothMoveCoroutine = StartCoroutine(SmoothMoveTo(
                desiredOrbitPos,
                initialTravelDuration,
                orientDuringMove: rotateToTangent,
                useCurve: true,
                curveStrength: initialCurveStrength,
                onComplete: () =>
                {
                    smoothMoveCoroutine = null;
                    isOrbiting = true;
                    isMoving = false;
                }));
        }
        else
        {
            isOrbiting = true;
            if (rotateToTangent) OrientToTangent();
            isMoving = false;
        }

        stepsTaken = 0;
        totalRotationProgress = 0f;
    }

    protected override void MoveFurther()
    {
        if (!this || gameObject == null) return;

        isMoving = false;
        if (!isOrbiting) return;

        // step angle
        float stepDeg = orbitStepSign * Mathf.Abs(degreesPerStep);
        currentAngleDeg += stepDeg;
        stepsTaken++;

        // track total rotation
        totalRotationProgress += Mathf.Abs(stepDeg);

        // orbit pos
        float rad = currentAngleDeg * Mathf.Deg2Rad;
        Vector3 newPos = orbitCenter + new Vector3(Mathf.Cos(rad), Mathf.Sin(rad), 0f) * orbitRadius;

        // smooth move
        isMoving = true;
        if (smoothMoveCoroutine != null)
        {
            StopCoroutine(smoothMoveCoroutine);
            smoothMoveCoroutine = null;
        }
        smoothMoveCoroutine = StartCoroutine(SmoothMoveTo(
            newPos,
            smoothStepDuration,
            orientDuringMove: rotateToTangent,
            useCurve: false,
            curveStrength: 0f,
            onComplete: () =>
            {
                smoothMoveCoroutine = null;
                isMoving = false;

                // step-based limit
                if (maxSteps > 0 && stepsTaken >= maxSteps)
                {
                    Destroy(gameObject);
                }

                // rotation-based limit
                if (maxRotations > 0 && totalRotationProgress >= 360f * maxRotations)
                {
                    Destroy(gameObject);
                }
            }));
    }

    private IEnumerator SmoothMoveTo(Vector3 target, float duration, bool orientDuringMove, bool useCurve = false, float curveStrength = 0.6f, System.Action onComplete = null)
    {
        Vector3 p0 = transform.position;
        Vector3 p2 = target;
        float t = 0f;
        float dur = Mathf.Max(0.0001f, duration);

        // control point
        Vector3 p1 = (p0 + p2) * 0.5f;
        if (useCurve)
        {
            Vector3 midRel = p1 - orbitCenter;
            Vector3 dirPerp;
            if (midRel.sqrMagnitude > 1e-6f)
            {
                Vector3 radial = midRel.normalized;
                dirPerp = new Vector3(-radial.y, radial.x, 0f);
                dirPerp *= -orbitStepSign;
            }
            else
            {
                Vector3 straight = (p2 - p0).normalized;
                dirPerp = new Vector3(-straight.y, straight.x, 0f) * -orbitStepSign;
            }
            float offset = orbitRadius * Mathf.Clamp01(curveStrength);
            p1 += dirPerp * offset;
        }

        while (t < dur)
        {
            if (this == null) yield break;
            t += Time.deltaTime;
            float raw = Mathf.Clamp01(t / dur);
            float f = Mathf.SmoothStep(0f, 1f, raw);

            if (useCurve)
            {
                float u = 1f - f;
                Vector3 pos = u * u * p0 + 2f * u * f * p1 + f * f * p2;
                transform.position = pos;
            }
            else
            {
                transform.position = Vector3.Lerp(p0, p2, f);
            }

            // update angle
            Vector3 rel = transform.position - orbitCenter;
            if (rel.sqrMagnitude > 1e-8f)
                currentAngleDeg = Mathf.Atan2(rel.y, rel.x) * Mathf.Rad2Deg;

            if (orientDuringMove)
                OrientToTangent();

            yield return null;
        }

        transform.position = p2;
        Vector3 finalRel = transform.position - orbitCenter;
        if (finalRel.sqrMagnitude > 1e-8f)
            currentAngleDeg = Mathf.Atan2(finalRel.y, finalRel.x) * Mathf.Rad2Deg;
        if (orientDuringMove) OrientToTangent();

        onComplete?.Invoke();
    }

    private void OrientToTangent()
    {
        float tangentDeg = currentAngleDeg + (orbitStepSign * 90f);
        transform.rotation = Quaternion.Euler(0f, 0f, tangentDeg);
    }

    protected override void OnTriggerEnter2D(Collider2D other)
    {
        if (other == null || other.gameObject == null) return;
        if (owner != null)
        {
            if (other.gameObject == owner || other.transform.IsChildOf(owner.transform))
                return;
        }

        var dmg = other.GetComponent<IDamageable>();
        if (dmg != null)
        {
            try
            {
                dmg.TakeDamage(damageAmount);
                Debug.Log($"[ChakramSpell] Dealt {damageAmount} damage to {other.gameObject.name} via OnTriggerEnter2D.");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[ChakramSpell] Exception calling TakeDamage on {other.gameObject.name}: {ex.Message}");
            }
        }
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.25f);
        Gizmos.DrawSphere(orbitCenter, 0.05f);
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.15f);
        Gizmos.DrawWireSphere(orbitCenter, Mathf.Max(cellSize, orbitRadius));
    }
#endif
}
