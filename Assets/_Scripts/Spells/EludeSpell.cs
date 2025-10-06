using UnityEngine;

/// <summary>
/// EludeSpell - owner-aware teleport helper with auto-initialize on Start.
/// Teleports the subject (owner or tagged Player) and, if present, also moves the subject's MovePoint
/// (child named "MovePoint" or the scene GameObject called "MovePoint") so movement logic stays in sync.
/// </summary>
public class EludeSpell : MonoBehaviour
{
    [Tooltip("How many tiles away to place the subject (owner or player).")]
    public int tilesAway = 3;

    [Tooltip("World units per tile (usually 1 if using 16px @ 16 PPU).")]
    public float tileWorldSize = 1f;

    [Tooltip("If true and dir is nearly zero, snap to a cardinal (4-way) direction using GlobalDirection.Direction.")]
    public bool snapToCardinalIfZero = true;

    [Tooltip("Optional: owner to teleport. If null, the object tagged 'Player' will be used. Can be set in inspector or at runtime before Start.")]
    public GameObject owner;

    [Tooltip("Optional: initial direction used by Start() auto-initialize if Initialize wasn't called manually. " +
             "Set to Vector3.zero to rely on GlobalDirection.Direction when snapToCardinalIfZero is true.")]
    public Vector3 initialDirection = Vector3.zero;

    // internal flag so we only initialize once
    private bool initialized = false;

    /// <summary>
    /// Initialize and perform the teleport.
    /// Returns true on success (teleport performed and helper destroyed), false otherwise.
    /// </summary>
    public bool Initialize(Vector3 dir, GameObject owner = null)
    {
        if (initialized)
        {
            Debug.LogWarning($"[EludeSpell] Initialize called but EludeSpell '{gameObject.name}' already initialized.");
            return false;
        }

        initialized = true;

        Debug.Log($"[EludeSpell] Initialize called on '{gameObject.name}' with dir={dir} owner={(owner ? owner.name : "null")}");

        // Determine the subject to move: prefer passed-in owner, otherwise use the serialized owner, then fallback to Player tag
        GameObject subject = owner != null ? owner : this.owner;
        if (subject == null)
        {
            subject = GameObject.FindGameObjectWithTag("Player");
            if (subject == null)
            {
                Debug.LogWarning("[EludeSpell] No owner provided and no GameObject tagged 'Player' found. Aborting and destroying helper.");
                Destroy(gameObject);
                return false;
            }
            Debug.Log($"[EludeSpell] No owner passed — using GameObject tagged 'Player': '{subject.name}'");
        }
        else
        {
            Debug.Log($"[EludeSpell] Using provided owner: '{subject.name}'");
        }

        // Determine direction (normalize or snap to cardinal)
        Vector2 direction;
        if (dir.sqrMagnitude > 1e-6f)
        {
            direction = dir.normalized;
        }
        else if (snapToCardinalIfZero)
        {
            direction = SnapToCardinalDirection(GlobalDirection.Direction);
            Debug.Log($"[EludeSpell] Input dir was zero: snapped to cardinal direction {direction}");
        }
        else
        {
            direction = Vector2.right;
            Debug.Log($"[EludeSpell] Input dir was zero and snap disabled: defaulting to {direction}");
        }

        // Compute destination (preserve subject's z)
        Vector3 subjectPos = subject.transform.position;
        Vector3 destination = subjectPos + (Vector3)(direction * (tilesAway * tileWorldSize));
        destination.z = subjectPos.z;

        Debug.Log($"[EludeSpell] Subject '{subject.name}' current pos = {subjectPos} -> destination = {destination}");

        // Teleport this helper object to the destination first (so it visually sits where the subject goes)
        transform.position = new Vector3(destination.x, destination.y, transform.position.z);
        Debug.Log($"[EludeSpell] Helper object '{gameObject.name}' moved to {transform.position}");

        // Move the subject's MovePoint (if any) before moving the subject so any movement system sees a consistent state.
        Transform subjectMovePoint = FindSubjectMovePoint(subject);
        if (subjectMovePoint != null)
        {
            // Preserve z on movePoint (usually 0)
            Vector3 mpPos = subjectMovePoint.position;
            subjectMovePoint.position = new Vector3(destination.x, destination.y, mpPos.z);
            Debug.Log($"[EludeSpell] Moved subject's MovePoint '{subjectMovePoint.name}' to {subjectMovePoint.position}");

            // If movePoint has a rigidbody, reset velocity
            var mpRb2d = subjectMovePoint.GetComponent<Rigidbody2D>();
            if (mpRb2d != null)
            {
                mpRb2d.velocity = Vector2.zero;
                Debug.Log($"[EludeSpell] Reset Rigidbody2D velocity on MovePoint '{subjectMovePoint.name}'");
            }
            var mpRb3d = subjectMovePoint.GetComponent<Rigidbody>();
            if (mpRb3d != null)
            {
                mpRb3d.velocity = Vector3.zero;
                Debug.Log($"[EludeSpell] Reset Rigidbody velocity on MovePoint '{subjectMovePoint.name}'");
            }
        }
        else
        {
            Debug.Log("[EludeSpell] No MovePoint found for subject — skipping MovePoint reposition.");
        }

        // Teleport the subject (owner/player) to the destination
        subject.transform.position = destination;
        Debug.Log($"[EludeSpell] Subject '{subject.name}' teleported to {subject.transform.position}");

        // If the subject has Rigidbody2D/Rigidbody, reset velocity to avoid sliding
        var rb2d = subject.GetComponent<Rigidbody2D>();
        if (rb2d != null)
        {
            rb2d.velocity = Vector2.zero;
            Debug.Log($"[EludeSpell] Reset Rigidbody2D velocity on '{subject.name}'");
        }
        var rb3d = subject.GetComponent<Rigidbody>();
        if (rb3d != null)
        {
            rb3d.velocity = Vector3.zero;
            Debug.Log($"[EludeSpell] Reset Rigidbody velocity on '{subject.name}'");
        }

        // Done — remove the helper
        Debug.Log($"[EludeSpell] Destroying helper '{gameObject.name}'");
        Destroy(gameObject);
        return true;
    }

    /// <summary>
    /// Try to find the MovePoint associated with 'subject'.
    /// 1) Try subject.transform.Find("MovePoint") (child).
    /// 2) Fallback to GameObject.Find("MovePoint") in scene.
    /// Returns null if none found.
    /// </summary>
    private Transform FindSubjectMovePoint(GameObject subject)
    {
        if (subject == null) return null;

        // 1) child search
        var child = subject.transform.Find("MovePoint");
        if (child != null) return child;

        // 2) fallback to scene-level MovePoint
        var sceneMP = GameObject.Find("MovePoint");
        if (sceneMP != null) return sceneMP.transform;

        // not found
        return null;
    }

    /// <summary>
    /// Auto-initialize in Start() if not manually initialized.
    /// Uses the public fields `initialDirection` and `owner` as inputs.
    /// </summary>
    private void Start()
    {
        if (!initialized)
        {
            Debug.Log($"[EludeSpell] Auto-initializing '{gameObject.name}' in Start()");
            Initialize(initialDirection, owner);
        }
    }

    // Copied the 4-way snapping logic so EludeSpell behaves like Spell.SnapToCardinalDirection
    private Vector2 SnapToCardinalDirection(Vector3 dir)
    {
        if (dir.sqrMagnitude < 1e-6f)
            return Vector2.right;

        float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;

        if (angle > -45f && angle <= 45f)
            return Vector2.right;
        else if (angle > 45f && angle <= 135f)
            return Vector2.up;
        else if (angle <= -45f && angle > -135f)
            return Vector2.down;
        else
            return Vector2.left;
    }

#if UNITY_EDITOR
    // Optional debug warning if object destroyed without being initialized (helps catch misuses in Editor)
    private void OnDestroy()
    {
        if (!initialized)
            Debug.LogWarning($"[EludeSpell] Helper '{gameObject.name}' destroyed without Initialize being called.");
    }
#endif
}
