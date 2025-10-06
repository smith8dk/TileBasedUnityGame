using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class LightningDamage : MonoBehaviour
{
    [Header("Damage Settings")]
    [Tooltip("How many points of damage to apply when something enters the hitbox.")]
    [SerializeField] private int damageAmount = 10;

    private Collider2D hitbox;

    private void Awake()
    {
        // Cache and configure the collider as a trigger, but start disabled
        hitbox = GetComponent<Collider2D>();
        hitbox.isTrigger = true;
        hitbox.enabled = false;
    }

    /// <summary>
    /// Called by an Animation Event on the first frame you want damage to register.
    /// </summary>
    public void EnableHitbox()
    {
        hitbox.enabled = true;
    }

    /// <summary>
    /// Called by an Animation Event on the last frame you want damage to register.
    /// </summary>
    public void DisableHitbox()
    {
        hitbox.enabled = false;
    }

    /// <summary>
    /// Called by an Animation Event on the final frame to tear down the prefab.
    /// </summary>
    public void DestroySelf()
    {
        Destroy(gameObject);
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        // One‐off damage when something enters the collider
        var damageable = other.GetComponent<IDamageable>();
        if (damageable != null)
        {
            damageable.TakeDamage(damageAmount);
        }
    }

    private void OnDisable()
    {
        // Safety: ensure it’s off if the GameObject is ever disabled
        if (hitbox != null)
            hitbox.enabled = false;
    }
}
