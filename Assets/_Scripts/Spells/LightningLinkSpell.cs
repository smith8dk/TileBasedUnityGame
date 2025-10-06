using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Stationary node which, on spawn, will detect one nearby LightningLinkSpell within detectionRadius.
/// If a partner is found it creates a visible lightning bolt between them, deals damage to IDamageable
/// objects intersecting that segment, then the bolt persists for boltLifetime seconds and destroys
/// both originating nodes when it expires.
/// </summary>
[RequireComponent(typeof(CircleCollider2D))]
public class LightningLinkSpell : MonoBehaviour
{
    [Tooltip("Detection radius for linking to other LightningLinkSpells.")]
    [SerializeField] private float detectionRadius = 3f;

    [Tooltip("Damage dealt by the lightning bolt to IDamageable targets.")]
    [SerializeField] private int boltDamage = 10;

    [Tooltip("Lifetime (seconds) that the bolt remains visible.")]
    [SerializeField] private float boltLifetime = 1f;

    [Tooltip("Thickness of the rendered lightning bolt.")]
    [SerializeField] private float boltWidth = 0.12f;

    [Tooltip("Optional jaggedness segments for bolt (0 = straight line)")]
    [SerializeField] private int boltSegments = 0;

    private CircleCollider2D detectionCollider;
    private bool hasLinked = false;

    private IEnumerator Start()
    {
        detectionCollider = GetComponent<CircleCollider2D>();
        if (detectionCollider != null)
        {
            detectionCollider.isTrigger = true;
            detectionCollider.radius = detectionRadius;
        }

        // wait one frame so other objects spawned this frame will exist
        yield return null;

        DetectAndLink();
    }

    /// <summary>
    /// Find a partner and create bolt (only one node does the creation via instanceId ordering).
    /// </summary>
    private void DetectAndLink()
    {
        if (hasLinked) return;

        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, detectionRadius);
        foreach (var hit in hits)
        {
            if (hit == null) continue;
            if (hit.gameObject == gameObject) continue;

            LightningLinkSpell other = hit.GetComponent<LightningLinkSpell>();
            if (other == null) continue;

            // avoid double-processing: only the node with smaller instanceID will perform the action
            if (this.GetInstanceID() >= other.GetInstanceID())
                continue;

            CreateBoltAndHoldPair(other);
            break;
        }
    }

    /// <summary>
    /// Create bolt visual, apply damage along the segment, then arrange for the bolt to destroy both nodes when it expires.
    /// </summary>
    private void CreateBoltAndHoldPair(LightningLinkSpell other)
    {
        if (other == null || hasLinked) return;

        // Mark both nodes as linked so they don't try to create another bolt
        hasLinked = true;
        other.hasLinked = true;

        Vector3 a = transform.position;
        Vector3 b = other.transform.position;

        // 1) Apply damage to IDamageable targets along the segment immediately
        ApplyDamageAlongSegment(a, b, boltDamage);

        // 2) Create visible bolt GameObject + LineRenderer
        GameObject boltGO = new GameObject("LightningBolt");
        var lr = boltGO.AddComponent<LineRenderer>();

        // Basic visible material (Sprites/Default works for simple colored lines)
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.widthMultiplier = boltWidth;
        lr.numCapVertices = 8;
        lr.numCornerVertices = 8;
        lr.sortingOrder = 1000;

        // color gradient (cyan -> white -> cyan)
        Gradient grad = new Gradient();
        grad.SetKeys(
            new GradientColorKey[] {
                new GradientColorKey(Color.cyan, 0f),
                new GradientColorKey(Color.white, 0.5f),
                new GradientColorKey(Color.cyan, 1f)
            },
            new GradientAlphaKey[] {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 0.5f),
                new GradientAlphaKey(1f, 1f)
            }
        );
        lr.colorGradient = grad;

        // set positions (straight or with simple jitter if boltSegments > 0)
        if (boltSegments <= 0)
        {
            lr.positionCount = 2;
            lr.SetPosition(0, a);
            lr.SetPosition(1, b);
        }
        else
        {
            lr.positionCount = boltSegments + 2;
            lr.SetPosition(0, a);
            for (int i = 0; i < boltSegments; i++)
            {
                float t = (float)(i + 1) / (boltSegments + 1);
                Vector3 pos = Vector3.Lerp(a, b, t);

                // simple perpendicular jitter (deterministic-ish)
                Vector3 dir = (b - a).normalized;
                Vector3 perp = Vector3.Cross(dir, Vector3.forward).normalized;
                float jitter = (Mathf.PerlinNoise(t * 10f, Time.time * 2f) - 0.5f) * 0.5f;
                pos += perp * jitter;
                lr.SetPosition(i + 1, pos);
            }
            lr.SetPosition(boltSegments + 1, b);
        }

        // 3) Attach LightningBolt helper to self-destruct the bolt prefab after boltLifetime,
        //    and instruct it to destroy the two origin nodes when it expires.
        var boltHelper = boltGO.AddComponent<LightningBolt>();
        boltHelper.Setup(boltLifetime, new GameObject[] { this.gameObject, other.gameObject });
    }

    /// <summary>
    /// Applies damage to any IDamageable found along the straight segment from a -> b.
    /// Uses Physics2D.LinecastAll and de-duplicates IDamageable targets.
    /// </summary>
    private void ApplyDamageAlongSegment(Vector3 a, Vector3 b, int damage)
    {
        var hits = Physics2D.LinecastAll(a, b);
        if (hits == null || hits.Length == 0) return;

        var damaged = new HashSet<IDamageable>();
        foreach (var h in hits)
        {
            if (h.collider == null) continue;
            var target = h.collider.GetComponent<IDamageable>();
            if (target != null && !damaged.Contains(target))
            {
                damaged.Add(target);
                try
                {
                    target.TakeDamage(damage);
                    Debug.Log($"[LightningLinkSpell] Dealt {damage} damage to {h.collider.name}");
                }
                catch (System.Exception ex)
                {
                    Debug.LogWarning($"[LightningLinkSpell] Exception when calling TakeDamage on {h.collider.name}: {ex.Message}");
                }
            }
        }
    }

    // Visualize detection radius in editor when selected
#if UNITY_EDITOR
    private void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0f, 0.6f, 1f, 0.15f);
        Gizmos.DrawSphere(transform.position, detectionRadius);
    }
#endif
}

/// <summary>
/// Simple helper that destroys the lightning bolt GameObject after a short lifetime.
/// Also destroys provided origin nodes when it expires.
/// </summary>
public class LightningBolt : MonoBehaviour
{
    private float lifetime = 1f;
    private GameObject[] originNodes;

    public void Setup(float lifetimeSeconds, GameObject[] origins)
    {
        lifetime = lifetimeSeconds;
        originNodes = origins;
        StartCoroutine(ExpireCoroutine());
    }

    private IEnumerator ExpireCoroutine()
    {
        yield return new WaitForSeconds(lifetime);

        // destroy origin nodes if they still exist
        if (originNodes != null)
        {
            foreach (var go in originNodes)
            {
                if (go != null)
                {
                    try { Destroy(go); }
                    catch { }
                }
            }
        }

        Destroy(gameObject);
    }
}
