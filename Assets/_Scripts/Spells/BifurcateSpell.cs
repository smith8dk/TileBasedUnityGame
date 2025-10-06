using System.Collections;
using UnityEngine;

/// <summary>
/// BifurcateSpell:
/// - On Initialize it travels initialTiles * cellSize using the Spell movepoint system.
/// - On the first MoveFurther() call it optionally plays an animation, then splits into two configured prefabs
///   that diverge by splitAngleDegrees.
/// - The children are started via their Spell.Initialize so they use the same movepoint turn-based movement.
/// - Both the seed and children are rotated so their +X faces their travel direction (assumes sprite faces right by default).
/// </summary>
public class BifurcateSpell : Spell
{
    [Header("Initial travel (tiles)")]
    [Tooltip("How many tiles to travel immediately when the spell is initialized.")]
    [SerializeField] private int initialTiles = 4;

    [Tooltip("World size representing a single tile.")]
    [SerializeField] private float cellSize = 1f;

    [Header("Split settings")]
    [Tooltip("Total divergence angle between the two split projectiles (degrees).")]
    [SerializeField] private float splitAngleDegrees = 30f;

    [Tooltip("Prefab to spawn for the left split (if null, falls back to this.gameObject).")]
    [SerializeField] private GameObject leftSplitPrefab;

    [Tooltip("Prefab to spawn for the right split (if null, falls back to this.gameObject).")]
    [SerializeField] private GameObject rightSplitPrefab;

    [Header("Child settings")]
    [Tooltip("If true and the spawned child is also a BifurcateSpell, mark it as already split (prevents recursion).")]
    [SerializeField] private bool preventRecursiveSplitOnChildren = true;

    [Header("Split animation (optional)")]
    [Tooltip("Animator on this GameObject whose animation should play when the split occurs (optional).")]
    [SerializeField] private Animator splitAnimator = null;

    [Tooltip("If set, the animator trigger to fire when the split occurs. If empty, animationStateName is used.")]
    [SerializeField] private string animationTrigger = "";

    [Tooltip("If set, the animator state name to play on split. Used to lookup clip length if animationDuration is zero.")]
    [SerializeField] private string animationStateName = "";

    [Tooltip("If > 0, use this duration (seconds) to wait for the split animation; otherwise attempt to read clip length from the controller.")]
    [SerializeField] private float animationDuration = 0f;

    // runtime
    private bool hasSplit = false;
    private bool pendingSplit = false; // if MoveFurther triggered while still moving, wait until arrival

    protected override void Start()
    {
        base.Start();
        // do not override isMoving here — Initialize will set movement
    }

    public override void Initialize(Vector3 dir, GameObject owner = null, GameObject sourceDraggable = null, int cooldownTurns = 0)
    {
        // standard owner logic
        this.owner = owner;
        ApplyOwnerCollisionIgnore();

        // direction: use passed dir or snap to cardinal if near-zero
        if (dir.sqrMagnitude > 1e-6f)
            direction = dir.normalized;
        else
            direction = SnapToCardinalDirection(GlobalDirection.Direction);

        // rotate prefab so +X faces travel direction (assumes sprite faces right by default)
        AlignToDirection(direction);

        // set start/target for movepoint system using a custom travel distance
        startPosition = transform.position;
        float travelDistance = Mathf.Max(0f, initialTiles * cellSize);
        currentSegmentLength = Mathf.Max(0.0001f, travelDistance);
        targetPosition = startPosition + (Vector3)(direction.normalized * currentSegmentLength);

        // ensure movePoint exists and point it at target
        if (movePoint != null)
            movePoint.position = targetPosition;
        else
        {
            movePoint = new GameObject("SpellMovepoint").transform;
            movePoint.position = targetPosition;
        }

        // start movement using the base system
        isMoving = true;
        moveTime = 0f;
        currentSpeed = baseSpeed;

        // reset split state
        hasSplit = false;
        pendingSplit = false;
    }

    protected override void Update()
    {
        // Use base Update so the movement interpolation and isMoving toggling works.
        base.Update();

        // If we were asked to split but the initial movement was still in progress,
        // wait until the object is no longer moving, then perform the split (with animation if assigned).
        if (pendingSplit && !hasSplit && !isMoving)
        {
            pendingSplit = false;
            StartSplitProcess();
        }
    }

    protected override void MoveFurther()
    {
        if (!this || gameObject == null) return;

        // If we haven't split yet, splitting should happen now.
        if (!hasSplit)
        {
            // If still moving to initial position, defer the split until movement completes.
            if (isMoving)
            {
                pendingSplit = true;
                return;
            }

            // Not moving — start split (play animation first if present)
            StartSplitProcess();
            return;
        }

        // If already split, this instance is likely gone (destroyed upon splitting).
        // Child spells (if also Spell-derived) will handle their own MoveFurther behavior.
    }

    /// <summary>
    /// Starts the split process: either play animation then split, or split immediately if no animator.
    /// </summary>
    private void StartSplitProcess()
    {
        if (hasSplit) return; // guard

        // mark as split in-progress to prevent reentry
        hasSplit = true;

        if (splitAnimator != null)
        {
            StartCoroutine(PlayAnimationAndThenSplit());
        }
        else
        {
            // no animator: split immediately
            DoSplit();
        }
    }

    private IEnumerator PlayAnimationAndThenSplit()
    {
        // Play animation (trigger or state)
        if (splitAnimator != null)
        {
            if (!string.IsNullOrEmpty(animationTrigger) && HasAnimatorTrigger(splitAnimator, animationTrigger))
            {
                splitAnimator.SetTrigger(animationTrigger);
            }
            else if (!string.IsNullOrEmpty(animationStateName))
            {
                splitAnimator.Play(animationStateName);
            }
            else
            {
                // fallback: play first state (play by layer 0 index 0)
                splitAnimator.Play(0, 0, 0f);
            }
        }

        // Determine wait duration
        float wait = animationDuration;
        if (wait <= 0f && splitAnimator != null && splitAnimator.runtimeAnimatorController != null)
        {
            // try to find a matching clip by name; otherwise use first clip length
            var clips = splitAnimator.runtimeAnimatorController.animationClips;
            if (!string.IsNullOrEmpty(animationStateName))
            {
                foreach (var c in clips)
                {
                    if (c != null && c.name == animationStateName)
                    {
                        wait = c.length;
                        break;
                    }
                }
            }
            if (wait <= 0f && clips != null && clips.Length > 0)
                wait = clips[0].length;
        }

        // final safety default
        if (wait <= 0f) wait = 0.45f;

        // wait the animation duration (so it plays in full)
        yield return new WaitForSeconds(wait);

        // Now perform split
        DoSplit();
    }

    private static bool HasAnimatorTrigger(Animator anim, string triggerName)
    {
        if (anim == null || string.IsNullOrEmpty(triggerName)) return false;
        foreach (var p in anim.parameters)
        {
            if (p.type == AnimatorControllerParameterType.Trigger && p.name == triggerName) return true;
        }
        return false;
    }

    private void DoSplit()
    {
        // compute half angle (we split symmetrically about the original direction)
        float halfAngle = splitAngleDegrees * 0.5f;

        // base direction as Vector3 for Quaternion rotations
        Vector3 baseDir3 = new Vector3(direction.x, direction.y, 0f);

        // left = rotate by +halfAngle (counter-clockwise)
        Vector3 leftDir = Quaternion.Euler(0f, 0f, halfAngle) * baseDir3;
        // right = rotate by -halfAngle (clockwise)
        Vector3 rightDir = Quaternion.Euler(0f, 0f, -halfAngle) * baseDir3;

        // spawn left child
        SpawnChild(leftSplitPrefab, leftDir);

        // spawn right child
        SpawnChild(rightSplitPrefab, rightDir);

        // finally destroy the original seed object
        try { Destroy(gameObject); } catch { }
    }

    private void SpawnChild(GameObject prefab, Vector3 dir)
    {
        // fallback to original gameObject if no prefab provided
        GameObject toSpawn = prefab != null ? prefab : this.gameObject;

        // instantiate at current position. We'll rotate it so its +X faces dir BEFORE Initialize.
        GameObject spawned = Instantiate(toSpawn, transform.position, Quaternion.identity);
        if (spawned == null) return;

        // rotate spawned so +X faces travel direction (sprite should face right by default)
        float rotDeg = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        spawned.transform.rotation = Quaternion.Euler(0f, 0f, rotDeg);

        // try to initialize as a Spell so it uses movepoint system
        var spellComp = spawned.GetComponent<Spell>();
        if (spellComp != null)
        {
            // If child is a BifurcateSpell and we want to avoid further splits, set its hasSplit flag
            var bif = spellComp as BifurcateSpell;
            if (bif != null && preventRecursiveSplitOnChildren)
            {
                // mark it so it won't split again
                bif.hasSplit = true;
            }

            // Initialize child to start movement in the split direction; pass same owner
            spellComp.Initialize(dir, this.owner);
        }
        else
        {
            // If prefab is not a Spell, simply move it visually one segment immediately using a short coroutine.
            // This is a fallback; prefer to supply Spell-derived prefabs.
            StartCoroutine(MoveNonSpellOneSegment(spawned, dir));
        }
    }

    private IEnumerator MoveNonSpellOneSegment(GameObject go, Vector3 dir)
    {
        if (go == null) yield break;

        Vector3 start = go.transform.position;
        Vector3 end = start + dir.normalized * cellSize * Mathf.Max(1, initialTiles); // try to mimic same initial travel
        float dur = 0.15f;
        float t = 0f;
        while (t < dur)
        {
            if (go == null) yield break;
            t += Time.deltaTime;
            go.transform.position = Vector3.Lerp(start, end, Mathf.Clamp01(t / dur));
            yield return null;
        }
        go.transform.position = end;
    }

    /// <summary>
    /// Rotate so +X faces the given direction (assumes sprite faces right by default).
    /// </summary>
    private void AlignToDirection(Vector2 dir)
    {
        if (dir.sqrMagnitude < 1e-6f) return;
        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
        transform.rotation = Quaternion.Euler(0f, 0f, angle);
    }
}
