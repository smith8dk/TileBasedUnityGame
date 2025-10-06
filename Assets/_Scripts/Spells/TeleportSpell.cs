using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class TeleportSpell : Spell
{
    [Header("Player References")]
    [Tooltip("(Optional) Drag in the PlayerController's movePoint transform here.\n" +
             "If left blank, at runtime we'll look for a GameObject called \"MovePoint\".")]
    [SerializeField] private Transform playerMovePoint;

    // Only one teleport-marker allowed at once
    private static TeleportSpell _activeMarker;

    private Collider2D _boxCollider;

    private void Awake()
    {
        _boxCollider = GetComponent<Collider2D>();
        _boxCollider.isTrigger = true;
        _boxCollider.enabled = false;

        // Snap this spell object onto the player immediately
        var playerGO = GameObject.FindGameObjectWithTag("Player");
        if (playerGO != null)
            transform.position = playerGO.transform.position;
        else
            Debug.LogWarning("[TeleportSpell] No GameObject tagged 'Player' found.");

        // If no movePoint was assigned in inspector, try to find one in the scene
        if (playerMovePoint == null)
        {
            var mpGO = GameObject.Find("MovePoint");
            if (mpGO != null)
                playerMovePoint = mpGO.transform;
            else
                Debug.LogWarning("[TeleportSpell] No \"MovePoint\" found in scene, and none assigned in inspector.");
        }
    }

    protected override void Start()
    {
        // Prevent this spell from moving
        isMoving       = false;
        startPosition  = transform.position;
        targetPosition = transform.position;
        moveTime       = 1f;
    }

    /// <summary>
    /// Initialize now accepts an optional owner so we can ignore collisions with the spawner.
    /// If there's already an active marker, do the teleport (same behavior as before).
    /// </summary>
    public override void Initialize(Vector3 dir, GameObject owner = null, GameObject sourceDraggable = null, int cooldownTurns = 0)
    {
        // If there's already an active marker, do the teleport instead
        if (_activeMarker != null)
        {
            // 1) Teleport the Player
            var playerGO = GameObject.FindGameObjectWithTag("Player");
            if (playerGO != null)
                playerGO.transform.position = _activeMarker.transform.position;

            // 2) Teleport their movePoint as well
            if (playerMovePoint != null)
                playerMovePoint.position = _activeMarker.transform.position;

            // 3) Destroy the old marker and clear
            Destroy(_activeMarker.gameObject);
            _activeMarker = null;

            // 4) Discard this new spell object
            Destroy(gameObject);
            return;
        }

        // Otherwise, this spell instance becomes the single active marker
        _activeMarker = this;

        // Snap to current player location
        var player = GameObject.FindGameObjectWithTag("Player");
        if (player != null)
            transform.position = player.transform.position;

        // Call base Initialize with owner so the Spell will record the owner and ignore its colliders
        base.Initialize(Vector3.zero, owner);

        // Lock in place
        isMoving       = false;
        startPosition  = transform.position;
        targetPosition = transform.position;
        moveTime       = 0f;
    }

    /// <summary>Enable the trigger (animation event).</summary>
    public void EnableboxCollider()
    {
        _boxCollider.enabled = true;
        // When enabling the collider at runtime, re-apply owner-ignore so the owner doesn't get hit.
        // ApplyOwnerCollisionIgnore is protected in Spell, so we can call it here.
        ApplyOwnerCollisionIgnore();
    }

    /// <summary>Disable the trigger (animation event).</summary>
    public void DisableboxCollider() => _boxCollider.enabled = false;

    protected override void OnDestroy()
    {
        base.OnDestroy();
        // If this marker is being destroyed, clear the static ref
        if (_activeMarker == this)
            _activeMarker = null;
    }
}
