using System.Collections;
using UnityEngine;

/// <summary>
/// ImpaleSpell:
/// - Positions itself spawnDistance away from the owner when Initialize(...) is called.
/// - Stays active (colliders enabled) until MoveFurther() is called.
/// - On MoveFurther(): disables colliders, fires the Animator trigger to play Retract, waits until Retract finishes, then destroys itself.
/// - The Animator controller should have a 'Spike' -> 'Retract' transition that triggers on the 'Retract' trigger (Has Exit Time unchecked, transition duration ~0).
/// </summary>
public class ImpaleSpell : Spell
{
    [Header("Impale settings")]
    [Tooltip("Distance (in world units) from the owner at which to position this ImpaleSpell.")]
    public float spawnDistance = 5f;

    [Header("Animator integration")]
    [Tooltip("Animator trigger name to request the retraction transition.")]
    public string retractTriggerName = "Retract";

    [Tooltip("Animator state name for the retract state (exact state/clip name).")]
    public string retractStateName = "Retract";

    [Tooltip("Max time (s) to wait for the controller to enter the retrat state before falling back to destroy.")]
    public float waitForStateTimeout = 0.5f;

    [Tooltip("Max time (s) to wait for the retract animation to finish once it starts before falling back to destroy.")]
    public float waitForFinishTimeout = 2.0f;

    private Animator animator;
    private bool isRetracting = false;
    private Coroutine retractCoroutine;

    protected override void Start()
    {
        baseSpeed = 0f;
        base.Start();
        isMoving = false;
        animator = GetComponent<Animator>();
    }

    public override void Initialize(Vector3 dir, GameObject owner = null, GameObject sourceDraggable = null, int cooldownTurns = 0)
    {
        this.owner = owner;
        baseSpeed = 0f;
        ApplyOwnerCollisionIgnore();

        if (dir.sqrMagnitude > 1e-6f)
            direction = dir.normalized;
        else
            direction = SnapToCardinalDirection(GlobalDirection.Direction);

        Vector3 origin = (owner != null) ? owner.transform.position : transform.position;
        Vector3 spawnPos = origin + (Vector3)(direction.normalized * spawnDistance);
        transform.position = spawnPos;

        // Do not alter transform.rotation; keep prefab's authored orientation
        isMoving = false;
        moveTime = 0f;

        // Ensure animator cached in case Start hasn't run
        if (animator == null) animator = GetComponent<Animator>();
    }

    protected override void MoveFurther()
    {
        // Called by stepping system to retract/destroy the impale
        if (isRetracting) return;
        isRetracting = true;

        // disable colliders immediately (impale inactive during retraction)
        foreach (var c in GetComponentsInChildren<Collider2D>(true))
            if (c != null) c.enabled = false;

        // ensure animator ref
        if (animator == null) animator = GetComponent<Animator>();

        if (animator == null)
        {
            // No animator: destroy immediately
            Destroy(gameObject);
            return;
        }

        // Start coroutine to trigger Retract and wait for completion
        if (retractCoroutine != null) StopCoroutine(retractCoroutine);
        retractCoroutine = StartCoroutine(TriggerRetractAndWait());
    }

    private IEnumerator TriggerRetractAndWait()
    {
        // Fire the trigger that your controller uses to transition Spike -> Retract
        animator.ResetTrigger(retractTriggerName);
        animator.SetTrigger(retractTriggerName);

        // Wait for the controller to actually enter the Retract state (bounded by timeout)
        int retractHash = Animator.StringToHash(retractStateName);
        float elapsed = 0f;
        bool entered = false;

        while (elapsed < waitForStateTimeout)
        {
            var state = animator.GetCurrentAnimatorStateInfo(0);
            if (state.shortNameHash == retractHash)
            {
                entered = true;
                break;
            }
            elapsed += Time.deltaTime;
            yield return null;
        }

        if (!entered)
        {
            Debug.LogWarning($"ImpaleSpell: Animator did not enter state '{retractStateName}' within {waitForStateTimeout}s. Destroying anyway.");
            Destroy(gameObject);
            yield break;
        }

        // Now wait until the retract state's normalizedTime reaches >= 1 (clip finished).
        elapsed = 0f;
        while (elapsed < waitForFinishTimeout)
        {
            var state = animator.GetCurrentAnimatorStateInfo(0);

            // ensure we're still in the retract state (if not, bail)
            if (state.shortNameHash != retractHash)
            {
                Debug.LogWarning($"ImpaleSpell: Animator left retract state unexpectedly (destroying).");
                break;
            }

            // normalizedTime goes from 0..1 for a single playback (values >1 if looping)
            if (state.normalizedTime >= 1f)
            {
                // finished playing
                break;
            }

            elapsed += Time.deltaTime;
            yield return null;
        }

        // optional tiny delay to let final frame render
        yield return null;

        Destroy(gameObject);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
    }
}
