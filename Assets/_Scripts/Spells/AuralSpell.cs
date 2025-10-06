using System.Collections;
using UnityEngine;

[RequireComponent(typeof(CircleCollider2D), typeof(SpriteRenderer))]
public class AuralSpell : Spell
{
    [Header("Collider Settings")]
    [Tooltip("Circle collider used to register damage and shockwave area")]
    [SerializeField] private CircleCollider2D circleCollider;

    [Header("ShockWave Settings (Shader)")]
    [Tooltip("Time (seconds) for each ripple segment to animate before pausing")]
    [SerializeField] private float segmentDuration = 0.25f;
    [Tooltip("Total time (seconds) for a full shockwave animation")]
    [SerializeField] private float totalWaveTime = 1f;

    [Header("Collider Expansion Steps")]
    [Tooltip("Amount to increase the collider radius each segment (in world units)")]
    [SerializeField] private float colliderStep = 0.3f;
    [Tooltip("Maximum collider radius (in world units)")]
    [SerializeField] private float maxColliderRadius = 0.5f;
    [Tooltip("Time (seconds) over which the collider smoothly expands to each new step")]
    [SerializeField] private float colliderExpandTime = 0.25f;

    private Coroutine waveCoroutine;
    private Coroutine colliderCoroutine;
    private Material materialInstance;
    private float elapsedWaveTime;
    private int expandCount = 0;
    private int lifetimeCounter = 4;

    // shader property ID
    private static readonly int WaveDistID = Shader.PropertyToID("_WaveDistanceFromCenter");

    private void Awake()
    {
        circleCollider = circleCollider ?? GetComponent<CircleCollider2D>();
        circleCollider.isTrigger = true;
        circleCollider.radius = 0f;
        circleCollider.enabled = false;

        var sr = GetComponent<SpriteRenderer>();
        materialInstance = Instantiate(sr.material);
        sr.material = materialInstance;
    }

    protected override void Start()
    {
        base.Start();
        isMoving = false;
        startPosition = targetPosition = transform.position;
        moveTime = 1f;
    }

    /// <summary>
    /// Initialize now accepts optional owner so spells won't hit their spawner.
    /// </summary>
    public override void Initialize(Vector3 dir, GameObject owner = null, GameObject sourceDraggable = null, int cooldownTurns = 0)
    {
        // Snap to player if available (original behavior)
        var player = GameObject.FindWithTag("Player");
        if (player != null)
            transform.position = player.transform.position;

        // Call base to record owner and apply owner-collision ignores
        base.Initialize(dir, owner);

        isMoving = false;
        startPosition = targetPosition = transform.position;
        moveTime = 0f;

        // enable collider and reset radius
        circleCollider.enabled = true;
        circleCollider.radius = 0f;
        expandCount = 0;

        // Ensure owner colliders are ignored after enabling collider
        ApplyOwnerCollisionIgnore();

        // start first segment and initial expansion
        elapsedWaveTime = 0f;
        StartWaveSegment();
        ExpandColliderStep();
    }

    protected override void OnDestroy()
    {
        // base handles removing from activeSpells and cleanup
        base.OnDestroy();
    }

    protected override void MoveFurther()
    {
        // advance wave and collider by one segment each turn
        StartWaveSegment();
        ExpandColliderStep();
    }

    private void StartWaveSegment()
    {
        if (waveCoroutine != null)
            StopCoroutine(waveCoroutine);
        waveCoroutine = StartCoroutine(WaveSegment());
    }

    private IEnumerator WaveSegment()
    {
        float end = Mathf.Min(elapsedWaveTime + segmentDuration, totalWaveTime);
        while (elapsedWaveTime < end)
        {
            elapsedWaveTime += Time.deltaTime;
            float tNorm = Mathf.Clamp01(elapsedWaveTime / totalWaveTime);
            materialInstance.SetFloat(WaveDistID, tNorm);
            yield return null;
        }
        waveCoroutine = null;
    }

    private void ExpandColliderStep()
    {
        expandCount++;
        float current = circleCollider.radius;
        float target = Mathf.Min(current + colliderStep, maxColliderRadius);
        if (colliderCoroutine != null)
            StopCoroutine(colliderCoroutine);
        colliderCoroutine = StartCoroutine(SmoothExpandCollider(current, target));
    }

    private IEnumerator SmoothExpandCollider(float startR, float endR)
    {
        float elapsed = 0f;
        while (elapsed < colliderExpandTime)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / colliderExpandTime);
            circleCollider.radius = Mathf.Lerp(startR, endR, t);
            yield return null;
        }
        circleCollider.radius = endR;
        colliderCoroutine = null;

        // after third expansion completed, disable and destroy
        if (expandCount >= lifetimeCounter)
        {
            circleCollider.enabled = false;
            DestroySelf();
        }
    }

    public void DestroySelf() => Destroy(gameObject);

    private void OnTriggerEnter2D(Collider2D other)
    {
        // Defensive: ignore owner's colliders (if somehow OnTrigger fires)
        if (owner != null)
        {
            if (other.gameObject == owner || other.transform.IsChildOf(owner.transform))
                return;
        }

        if (other.TryGetComponent<IDamageable>(out var dmg))
            dmg.TakeDamage(damageAmount);
    }
}
