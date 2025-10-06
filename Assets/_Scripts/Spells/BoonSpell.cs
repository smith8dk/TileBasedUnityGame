using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// A stationary "boon" spell. Inherits Spell, but does not move (baseSpeed = 0).
/// Interacting with objects tagged "Spell" will double that spell's damage once
/// and show an outline. Works even if both colliders are triggers.
/// </summary>
public class BoonSpell : Spell
{
    [Tooltip("Color used for the outline when the boon is activated.")]
    [SerializeField] private Color outlineColor = Color.yellow;

    [Tooltip("Scale multiplier for the outline sprite (1 = same size).")]
    [SerializeField] private float outlineScale = 1.12f;

    [Tooltip("Radius used to check for overlapping Spell objects.")]
    [SerializeField] private float detectionRadius = 0.5f;

    [Tooltip("Maximum number of spells this boon can buff before expiring.")]
    [SerializeField] private int maxBuffs = 3;

    private SpriteRenderer mainSpriteRenderer;
    private SpriteRenderer outlineSpriteRenderer;

    private HashSet<Spell> buffedSpells = new HashSet<Spell>();
    private int buffsUsed = 0;

    protected override void Start()
    {
        // Force stationary
        baseSpeed = 0f;
        currentSpeed = 0f;
        isMoving = false;

        mainSpriteRenderer = GetComponent<SpriteRenderer>();
        CreateOutlineSprite();

        base.Start();

        // reset any motion state
        isMoving = false;
        moveTime = 0f;
        currentSegmentLength = 0f;
        if (movePoint != null)
            movePoint.position = transform.position;
    }

    protected override void Update()
    {
        // Lock movement
        isMoving = false;

        base.Update();

        CheckForSpellOverlaps();
    }

    public override void Initialize(Vector3 dir, GameObject owner = null, GameObject sourceDraggable = null, int cooldownTurns = 0)
    {
        this.owner = owner;
        ApplyOwnerCollisionIgnore();

        direction = Vector3.zero;
        baseSpeed = 0f;
        currentSpeed = 0f;
        isMoving = false;
        moveTime = 0f;
        currentSegmentLength = 0f;

        if (movePoint != null)
            movePoint.position = transform.position;
    }

    protected override void MoveFurther()
    {
        // BoonSpell never moves
        isMoving = false;
    }

    private void CheckForSpellOverlaps()
    {
        if (buffsUsed >= maxBuffs)
            return; // already reached limit

        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, detectionRadius);

        foreach (var hit in hits)
        {
            if (hit.CompareTag("Spell") && hit.gameObject != gameObject)
            {
                Spell otherSpell = hit.GetComponent<Spell>();
                if (otherSpell != null && !buffedSpells.Contains(otherSpell))
                {
                    buffedSpells.Add(otherSpell);
                    buffsUsed++;

                    otherSpell.damageAmount *= 2;
                    ShowOutline();

                    Debug.Log($"[BoonSpell] Buff applied to {otherSpell.name}. New damage = {otherSpell.damageAmount} (buff {buffsUsed}/{maxBuffs})");

                    if (buffsUsed >= maxBuffs)
                    {
                        Debug.Log("[BoonSpell] Max buffs reached. Destroying boon.");
                        Destroy(gameObject);
                        return;
                    }
                }
            }
        }
    }

    private void CreateOutlineSprite()
    {
        if (mainSpriteRenderer == null) return;

        GameObject outlineGO = new GameObject("Outline");
        outlineGO.transform.SetParent(transform, false);
        outlineGO.transform.localPosition = Vector3.zero;
        outlineGO.transform.localRotation = Quaternion.identity;
        outlineGO.transform.localScale = Vector3.one * outlineScale;

        outlineSpriteRenderer = outlineGO.AddComponent<SpriteRenderer>();
        outlineSpriteRenderer.sprite = mainSpriteRenderer.sprite;
        outlineSpriteRenderer.sortingLayerID = mainSpriteRenderer.sortingLayerID;
        outlineSpriteRenderer.sortingOrder = mainSpriteRenderer.sortingOrder - 1;
        outlineSpriteRenderer.color = outlineColor;
        outlineSpriteRenderer.enabled = false;
    }

    private void ShowOutline()
    {
        if (outlineSpriteRenderer != null)
            outlineSpriteRenderer.enabled = true;
        else if (mainSpriteRenderer != null)
            mainSpriteRenderer.color = outlineColor;
    }

#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, detectionRadius);
    }
#endif
}
