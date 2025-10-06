using System.Collections;
using UnityEngine;

/// <summary>
/// Explosion effect handler:
/// - Plays the attached Animator clip when instantiated
/// - Damages IDamageable objects on trigger enter
/// - Auto-destroys after the animation finishes
/// </summary>
[RequireComponent(typeof(Animator))]
[RequireComponent(typeof(Collider2D))]
public class ExplosionVFX : MonoBehaviour
{
    public int damageAmount = 40;
    public float lifetime = 1f; // fallback lifetime if animator isn't used

    private Animator animator;

    private void Awake()
    {
        animator = GetComponent<Animator>();

        // Ensure collider is trigger
        var col = GetComponent<Collider2D>();
        col.isTrigger = true;
    }

    private void Start()
    {
        // Play default animation state
        if (animator != null)
        {
            AnimatorClipInfo[] clips = animator.GetCurrentAnimatorClipInfo(0);
            if (clips.Length > 0)
            {
                // destroy when animation finishes
                Destroy(gameObject, clips[0].clip.length);
            }
            else
            {
                Destroy(gameObject, lifetime);
            }
        }
        else
        {
            Destroy(gameObject, lifetime);
        }
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        var damageable = other.GetComponent<IDamageable>();
        if (damageable != null)
        {
            damageable.TakeDamage(damageAmount);
            Debug.Log($"Explosion hit {other.name} for {damageAmount} damage!");
        }
    }
}
