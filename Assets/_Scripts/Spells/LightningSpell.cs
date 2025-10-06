using UnityEngine;
using UnityEngine.InputSystem;

public class LightningSpell : Spell
{
    [Header("Lightning Effects")]

    [Tooltip("Prefab to spawn when the environment turn completes.")]
    [SerializeField]
    private GameObject effectPrefabOnDestroy;

    [Header("Spawn Alignment")]

    [Tooltip("If you want to manually nudge the effect up/down/right/left from the center.")]
    [SerializeField]
    private Vector3 manualSpawnOffset = Vector3.zero;

    [Tooltip("Automatically align the effect so its top edge sits at the spell’s position.")]
    [SerializeField]
    private bool autoAlignTop = true;

    protected override void Start()
    {
        // **Make sure to run your base class Start** so that
        // your lightning spells get added into activeSpells:
        activeSpells.Add(this);
        isMoving = false;

        if (effectPrefabOnDestroy == null)
            Debug.LogWarning($"[{nameof(LightningSpell)}] No effect prefab assigned – the spell will just disappear.");
    }
    protected override void MoveFurther()
    {
        Debug.Log("Lightning Moved");
        // this will be called when Spell.MoveSpells() runs on the turn event
        if (effectPrefabOnDestroy != null)
        {
            var effect = Instantiate(effectPrefabOnDestroy, transform.position, Quaternion.identity);
            effect.transform.position += manualSpawnOffset;
            if (autoAlignTop) AlignEffectTop(effect);
        }
        Destroy(gameObject);
    }

    private void AlignEffectTop(GameObject effect)
    {
        // Look for a SpriteRenderer (or ParticleSystemRenderer, etc.)
        // here we check SpriteRenderer first
        var sr = effect.GetComponentInChildren<SpriteRenderer>();
        if (sr != null)
        {
            // bounds.size.y gives total height in world units
            float halfHeight = sr.bounds.size.y * 0.5f;
            Vector3 p = effect.transform.position;
            // Move the pivot up so the top of the sprite is at the original spawn Y
            effect.transform.position = new Vector3(p.x, p.y + halfHeight, p.z);
            return;
        }

        // Fallback: if you have a particle system or other renderer, you could check
        // var pr = effect.GetComponentInChildren<ParticleSystemRenderer>();
        // ... similar logic ...

        // If no renderer found, do nothing
    }
}
