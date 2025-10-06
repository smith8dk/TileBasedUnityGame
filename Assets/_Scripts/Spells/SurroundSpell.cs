using UnityEngine;

/// <summary>
/// SurroundSpell:
/// - Moves forward as a standard Spell (one segment per MoveFurther).
/// - OnTriggerEnter2D: if hitting an enemy (enemyLayer), applies damage and spawns orbiting shards around the hit target.
/// - Then the projectile destroys itself.
/// </summary>
public class SurroundSpell : Spell
{
    [Header("Surround Settings")]
    [Tooltip("Prefab for the orbiting shard. Must have OrbitingProjectile component (inherits Spell).")]
    public GameObject orbitingShardPrefab;

    [Tooltip("How many shards to spawn around the target.")]
    public int shardCount = 4;

    [Tooltip("Radius (world units) of the orbit around the target.")]
    public float orbitRadius = 1.0f;

    [Tooltip("Degrees each shard rotates per MoveFurther() (per-turn rotation).")]
    public float rotateDegreesPerTurn = 45f;

    [Tooltip("How many MoveFurther() calls the orbiting shards live for.")]
    public int shardLifetimeTurns = 3;

    [Tooltip("If true, this SurroundSpell also damages the hit target before spawning shards.")]
    public bool damageOnImpact = true;

    protected override void Start()
    {
        base.Start();
    }

    protected override void OnTriggerEnter2D(Collider2D other)
    {
        // Defensive: ignore owner's colliders
        if (owner != null)
        {
            if (other.gameObject == owner || other.transform.IsChildOf(owner.transform))
                return;
        }

        // If the collided object is on the enemy layer, handle spawn
        int otherLayerBit = 1 << other.gameObject.layer;
        if ((enemyLayer.value & otherLayerBit) != 0)
        {
            // Optionally apply damage to the target
            if (damageOnImpact)
            {
                var damageable = other.GetComponent<IDamageable>();
                if (damageable != null)
                    damageable.TakeDamage(damageAmount);
            }

            // Spawn orbiting shards around the collided object's transform
            SpawnOrbitingShards(other.transform);

            // Destroy this projectile
            Destroy(gameObject);
            return;
        }

        // Otherwise fallback to default behavior (damage via IDamageable and destroy)
        base.OnTriggerEnter2D(other);
    }

    private void SpawnOrbitingShards(Transform target)
    {
        if (orbitingShardPrefab == null)
        {
            Debug.LogWarning("SurroundSpell: orbitingShardPrefab not assigned; cannot spawn shards.");
            return;
        }

        if (shardCount <= 0)
            return;

        for (int i = 0; i < shardCount; i++)
        {
            float angle = (360f / shardCount) * i;
            GameObject shardGO = Instantiate(orbitingShardPrefab, target.position, Quaternion.identity);

            var orbitComp = shardGO.GetComponent<OrbitingProjectile>();
            if (orbitComp != null)
            {
                // Use the new InitializeOrbit signature that accepts owner and inherited damage
                orbitComp.InitializeOrbit(
                    target: target,
                    initialAngleDeg: angle,
                    orbitRadius: orbitRadius,
                    degPerTurn: rotateDegreesPerTurn,
                    lifeTurns: shardLifetimeTurns,
                    owner: this.owner,
                    inheritedDamage: this.damageAmount
                );
            }
            else
            {
                // If prefab doesn't contain OrbitingProjectile, just place it and warn
                shardGO.transform.position = (Vector3)target.position + Quaternion.Euler(0, 0, angle) * Vector3.right * orbitRadius;
                Debug.LogWarning("SurroundSpell: spawned shard prefab missing OrbitingProjectile component.");
            }
        }
    }
}
