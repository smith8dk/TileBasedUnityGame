using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// LevelSpell: planted immobile object that advances through animation "stages" each MoveFurther() call.
/// After a configurable number of steps it explodes (3x3 damage) and transitions animation to Exit at the same time.
/// During the explosion the LevelSpell visuals are hidden while the object remains active for cleanup.
/// This version enforces NO rotation on the LevelSpell or the spawned explosion prefab.
/// </summary>
public class LevelSpell : Spell
{
    [Header("Timing / Explosion")]
    [Tooltip("How many MoveFurther() calls (turns) before the planted object explodes.")]
    [SerializeField] private int turnsToExplode = 3;

    [Tooltip("World size (tile) used to compute the 3x3 area. Typically 1.0 for tile-centered grids.")]
    [SerializeField] private float cellSize = 1f;

    // default set to 0.6f
    [Tooltip("How long the explosion VFX stays visible (seconds). The planted object will be destroyed after this time so Exit animation can play.")]
    [SerializeField] private float explosionDuration = 0.6f;

    [Header("VFX")]
    [Tooltip("Prefab that visually represents the explosion. If null the script uses a small fallback visual.")]
    [SerializeField] private GameObject explosionPrefab;

    [Header("Animator / Stage visuals")]
    [Tooltip("Optional Animator to control the LevelSpell visuals. If null, the script will try to GetComponent<Animator>().")]
    [SerializeField] private Animator animator;

    [Tooltip("If your animator does not expose an int 'Stage' parameter use these state names instead (Stage1, Stage2, Stage3).")]
    [SerializeField] private string[] stageStateNames = new string[] { "Level", "Level 2", "Level 3" };

    [Tooltip("Name of the Exit state in the animator (played when explosion happens).")]
    [SerializeField] private string exitStateName = "Exit";

    [Tooltip("Optional trigger name to fire on explosion instead of playing Exit state. If non-empty the script will call animator.SetTrigger(explodeTriggerName).")]
    [SerializeField] private string explodeTriggerName = "Explode";

    // internal turn counter
    private int turnCounter = 0;

    // small guard to ensure we only explode once
    private bool hasExploded = false;

    // cached availability of animator parameter
    private bool animatorHasStageIntParam = false;

    // convenience: number of visual stages (defaults to length of stageStateNames)
    private int visualStages => Mathf.Max(1, stageStateNames?.Length ?? 1);

    protected override void Start()
    {
        base.Start();

        // Ensure planted immobile behavior
        baseSpeed = 0f;
        currentSpeed = 0f;
        isMoving = false;

        // force the LevelSpell to have identity rotation (no facing applied)
        transform.rotation = Quaternion.identity;

        // auto-find Animator if not set
        if (animator == null)
            animator = GetComponent<Animator>();

        // detect whether animator has an int parameter named "Stage"
        animatorHasStageIntParam = false;
        if (animator != null)
        {
            foreach (var p in animator.parameters)
            {
                if (p.type == AnimatorControllerParameterType.Int && p.name == "Stage")
                {
                    animatorHasStageIntParam = true;
                    break;
                }
            }
        }

        // Start visuals at stage 1 (looping)
        ApplyStageVisual(1);
    }

    public override void Initialize(Vector3 dir, GameObject owner = null, GameObject sourceDraggable = null, int cooldownTurns = 0)
    {
        // Use owner logic from Spell to avoid hurting the caster
        this.owner = owner;
        ApplyOwnerCollisionIgnore();

        // Ensure immobile
        direction = Vector2.zero;
        startPosition = transform.position;
        currentSegmentLength = 0f;
        targetPosition = startPosition;
        if (movePoint != null) movePoint.position = transform.position;

        isMoving = false;
        moveTime = 0f;
        baseSpeed = 0f;
        currentSpeed = 0f;

        // reset counters (in case reused)
        turnCounter = 0;
        hasExploded = false;

        // ensure visuals reflect reset
        ApplyStageVisual(1);

        // Ensure the object is visible when (re)initialized
        SetVisualsEnabled(true);

        // Enforce no rotation on initialization (explicit)
        transform.rotation = Quaternion.identity;
    }

    // Prevent base Spell from damaging / destroying the planted object on collisions/triggers.
    protected override void OnTriggerEnter2D(Collider2D other)
    {
        // Ignore owner; otherwise do nothing so planted object remains until the timed explosion.
        if (owner != null && (other.gameObject == owner || other.transform.IsChildOf(owner.transform)))
            return;
        // intentionally ignore all other triggers (no damage / destroy).
    }

    protected override void OnCollisionEnter2D(Collision2D collision)
    {
        // Ignore owner; otherwise do nothing.
        if (owner != null && (collision.gameObject == owner || collision.transform.IsChildOf(owner.transform)))
            return;
        // intentionally ignore collisions.
    }

    protected override void MoveFurther()
    {
        if (!this || gameObject == null) return;

        // remain immobile
        isMoving = false;

        if (hasExploded) return;

        // increment turn counter
        turnCounter++;

        // If this is the final turn => explode and transition to Exit at the same time.
        if (turnCounter >= Mathf.Max(1, turnsToExplode))
        {
            // Move visual to Exit (so object leaves Stage3 as explosion happens)
            TriggerExitVisual();

            // explode & cleanup (spawns VFX and destroys object after explosionDuration)
            StartCoroutine(DoExplodeAndCleanup());
            return;
        }

        // Otherwise, advance to next stage (1 -> 2 -> 3)
        int stageToShow = Mathf.Min(1 + turnCounter, visualStages);
        ApplyStageVisual(stageToShow);
    }

    private IEnumerator DoExplodeAndCleanup()
    {
        if (hasExploded) yield break;
        hasExploded = true;

        // do damage immediately
        Explode();

        // spawn VFX WITHOUT applying any rotation (Quaternion.identity)
        GameObject vfx = null;
        if (explosionPrefab != null)
        {
            vfx = Instantiate(explosionPrefab, transform.position, Quaternion.identity);
        }
        else
        {
            vfx = CreateExplosionVFXFallback();
        }

        // hide this object's visuals (not disabling GameObject so coroutine survives)
        HideVisualsButKeepAlive();

        // wait for explosion effect duration
        if (explosionDuration > 0f)
            yield return new WaitForSeconds(explosionDuration);

        // clean up explosion VFX
        if (vfx != null)
            Destroy(vfx);

        // finally destroy the planted object
        Destroy(gameObject);
    }

    private void HideVisualsButKeepAlive()
    {
        // disable all renderers
        foreach (var r in GetComponentsInChildren<Renderer>())
        {
            if (r != null) r.enabled = false;
        }
        // disable colliders if you don’t want the object blocking during explosion
        foreach (var c in GetComponentsInChildren<Collider2D>())
        {
            c.enabled = false;
        }
    }

    /// <summary>
    /// Applies damage to IDamageable targets in a 3x3 grid centered on this object's position.
    /// Uses Physics2D.OverlapBoxAll and deduplicates targets.
    /// </summary>
    private void Explode()
    {
        Vector2 center = transform.position;
        Vector2 boxSize = new Vector2(cellSize * 3f, cellSize * 3f);

        Collider2D[] hits = Physics2D.OverlapBoxAll(center, boxSize, 0f);
        if (hits == null || hits.Length == 0) return;

        var damaged = new HashSet<IDamageable>();
        foreach (var hit in hits)
        {
            if (hit == null) continue;

            // Skip the owner if present
            if (owner != null && (hit.gameObject == owner || hit.transform.IsChildOf(owner.transform)))
                continue;

            var target = hit.GetComponent<IDamageable>();
            if (target != null && !damaged.Contains(target))
            {
                damaged.Add(target);
                try
                {
                    target.TakeDamage(damageAmount);
                    Debug.Log($"[LevelSpell] {hit.name} took {damageAmount} damage from explosion.");
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[LevelSpell] Exception calling TakeDamage on {hit.name}: {ex.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Apply visual for the given stage number (1-based).
    /// Tries three approaches:
    ///  - if animator has int param "Stage", set it.
    ///  - otherwise if animator exists, Play() the state name from stageStateNames array (index stage-1).
    ///  - otherwise nothing (no animator).
    /// </summary>
    private void ApplyStageVisual(int stage)
    {
        if (animator == null) return;

        // clamp stage
        int s = Mathf.Clamp(stage, 1, visualStages);

        if (animatorHasStageIntParam)
        {
            animator.SetInteger("Stage", s);
        }
        else
        {
            int idx = Mathf.Clamp(s - 1, 0, stageStateNames.Length - 1);
            string name = stageStateNames[idx];
            if (!string.IsNullOrEmpty(name))
            {
                // use Play so it immediately jumps to the state (helpful for turn-based visuals)
                animator.Play(name);
            }
        }
    }

    /// <summary>
    /// Trigger Exit/Explode visual. If explodeTriggerName is set it'll trigger that, otherwise it will Play the exitStateName.
    /// </summary>
    private void TriggerExitVisual()
    {
        if (animator == null) return;

        if (!string.IsNullOrEmpty(explodeTriggerName) && HasAnimatorTrigger(animator, explodeTriggerName))
        {
            animator.SetTrigger(explodeTriggerName);
        }
        else if (!string.IsNullOrEmpty(exitStateName))
        {
            animator.Play(exitStateName);
        }
    }

    // small helper: detect if animator has a trigger param of the given name
    private static bool HasAnimatorTrigger(Animator anim, string triggerName)
    {
        if (anim == null || string.IsNullOrEmpty(triggerName)) return false;
        foreach (var p in anim.parameters)
        {
            if (p.type == AnimatorControllerParameterType.Trigger && p.name == triggerName) return true;
        }
        return false;
    }

    /// <summary>
    /// Simple rectangular fallback VFX if no prefab assigned.
    /// </summary>
    private GameObject CreateExplosionVFXFallback()
    {
        Vector3 center = transform.position;
        Vector2 half = new Vector2(cellSize * 3f * 0.5f, cellSize * 3f * 0.5f);

        Vector3 bl = center + new Vector3(-half.x, -half.y, 0f);
        Vector3 tl = center + new Vector3(-half.x, half.y, 0f);
        Vector3 tr = center + new Vector3(half.x, half.y, 0f);
        Vector3 br = center + new Vector3(half.x, -half.y, 0f);

        GameObject go = new GameObject("LevelSpell_ExplosionVFX_Fallback");
        var lr = go.AddComponent<LineRenderer>();
        lr.positionCount = 5;
        lr.useWorldSpace = true;
        lr.loop = false;
        lr.widthMultiplier = 0.06f;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.numCornerVertices = 4;
        lr.numCapVertices = 4;
        lr.sortingOrder = 2000;

        lr.startColor = Color.red;
        lr.endColor = Color.yellow;

        lr.SetPosition(0, bl);
        lr.SetPosition(1, tl);
        lr.SetPosition(2, tr);
        lr.SetPosition(3, br);
        lr.SetPosition(4, bl);

        return go;
    }

    private void SetVisualsEnabled(bool enabled)
    {
        var renderers = GetComponentsInChildren<Renderer>(true);
        foreach (var r in renderers)
            if (r != null) r.enabled = enabled;

        var canvasRenderers = GetComponentsInChildren<CanvasRenderer>(true);
        foreach (var cr in canvasRenderers)
            if (cr != null) cr.SetAlpha(enabled ? 1f : 0f);
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(1f, 0.2f, 0.2f, 0.25f);
        Vector3 center = transform.position;
        Vector3 size = new Vector3(cellSize * 3f, cellSize * 3f, 0f);
        Gizmos.DrawCube(center, size);
    }
#endif
}
