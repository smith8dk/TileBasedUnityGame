using System.Collections;
using UnityEngine;

/// <summary>
/// SplitSpell:
/// - A simple Spell subclass used for the two projectiles spawned by BifurcateSpell.
/// - On Initialize(dir, owner) it sets up movepoint/target so the base Spell.Update interpolation
///   will move it toward targetPosition automatically.
/// - On MoveFurther() it advances one step (stepDistance) in the configured direction and restarts movement.
/// - Supports an optional maxSteps after which the object destroys itself.
/// - Uses base OnTriggerEnter2D for damage handling (inherited from Spell).
/// - Rotates to face its movement direction (sprite assumed to face +X/right by default).
/// - Plays "Pulse_Destroy" animation (trigger = "Destroy") before destruction.
/// </summary>
public class SplitSpell : Spell
{
    [Header("Movement")]
    [Tooltip("How far this projectile moves each MoveFurther step (world units).")]
    [SerializeField] private float stepDistance = 1f;

    [Tooltip("Maximum number of MoveFurther steps before auto-destroy. 0 = infinite.")]
    [SerializeField] private int maxSteps = 6;

    [Header("Destruction Animation")]
    [Tooltip("Animator with a trigger called 'Destroy' for the Pulse_Destroy animation.")]
    [SerializeField] private Animator anim;

    [Tooltip("Name of the destroy trigger parameter in the Animator.")]
    [SerializeField] private string destroyTrigger = "Destroy";

    [Tooltip("Duration (seconds) of the Pulse_Destroy animation. If 0, will auto-detect from Animator clips.")]
    [SerializeField] private float destroyAnimDuration = 0f;

    // runtime
    private int stepsTaken = 0;
    private bool isBeingDestroyed = false;

    protected override void Start()
    {
        base.Start();
        // ensure movePoint is in a sane location
        if (movePoint != null)
            movePoint.position = transform.position;
    }

    /// <summary>
    /// Initialize the spell with a direction and optional owner.
    /// This arranges the movepoint/target so the object travels one initial segment immediately.
    /// </summary>
    public override void Initialize(Vector3 dir, GameObject owner = null, GameObject sourceDraggable = null, int cooldownTurns = 0)
    {
        // owner logic (collision ignore)
        this.owner = owner;
        ApplyOwnerCollisionIgnore();

        // compute direction (snap to cardinal if near-zero)
        if (dir.sqrMagnitude > 1e-6f)
            direction = dir.normalized;
        else
            direction = SnapToCardinalDirection(GlobalDirection.Direction);

        // orient to face movement direction (sprite faces +X by default)
        AlignToDirection(direction);

        // Setup movepoint / initial travel so Spell.Update will move us smoothly
        startPosition = transform.position;
        currentSegmentLength = Mathf.Max(0.0001f, stepDistance);
        targetPosition = startPosition + (Vector3)(direction.normalized * currentSegmentLength);

        if (movePoint != null)
            movePoint.position = targetPosition;
        else
        {
            // defensive: ensure movePoint exists
            movePoint = new GameObject("SpellMovepoint").transform;
            movePoint.position = targetPosition;
        }

        // begin movement using base system
        isMoving = true;
        moveTime = 0f;
        currentSpeed = baseSpeed;

        // reset counters
        stepsTaken = 0;
    }

    /// <summary>
    /// Called by the turn system. Advance one step along the set direction.
    /// </summary>
    protected override void MoveFurther()
    {
        if (!this || gameObject == null || isBeingDestroyed) return;

        // increment steps counter
        stepsTaken++;

        // Ensure projectile faces the direction it's heading
        AlignToDirection(direction);

        // Prepare the next segment
        startPosition = transform.position;
        currentSegmentLength = Mathf.Max(0.0001f, stepDistance);
        targetPosition = startPosition + (Vector3)(direction.normalized * currentSegmentLength);

        if (movePoint != null)
            movePoint.position = targetPosition;

        // start movement; base.Update() will animate it
        isMoving = true;
        moveTime = 0f;
        currentSpeed = baseSpeed;

        // check lifetime
        if (maxSteps > 0 && stepsTaken >= maxSteps)
        {
            PlayDestroyAnimation();
        }
    }

    /// <summary>
    /// Rotate the transform so its +X faces the provided direction.
    /// </summary>
    private void AlignToDirection(Vector2 dir)
    {
        if (dir.sqrMagnitude < 1e-6f) return;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);
    }

    /// <summary>
    /// Plays Pulse_Destroy animation before destroying the object.
    /// </summary>
    private void PlayDestroyAnimation()
    {
        if (isBeingDestroyed) return;
        isBeingDestroyed = true;

        if (anim != null)
        {
            // fire the trigger
            anim.SetTrigger(destroyTrigger);

            // find clip length if not manually set
            float duration = destroyAnimDuration;
            if (duration <= 0f && anim.runtimeAnimatorController != null)
            {
                foreach (var clip in anim.runtimeAnimatorController.animationClips)
                {
                    if (clip != null && clip.name == "Pulse_Destroy")
                    {
                        duration = clip.length;
                        break;
                    }
                }
            }
            if (duration <= 0f) duration = 0.5f;

            // run delayed destruction
            StartCoroutine(DestroyAfterDelay(duration));
        }
        else
        {
            Destroy(gameObject);
        }
    }

    private System.Collections.IEnumerator DestroyAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        if (this && gameObject != null)
            Destroy(gameObject);
    }

    // We intentionally do not override OnTriggerEnter2D here — we rely on Spell.OnTriggerEnter2D
    // to handle IDamageable hits and destroying the projectile on contact. If destruction occurs there,
    // the base may call Destroy() directly. You can instead hook into that flow and call PlayDestroyAnimation().
}
