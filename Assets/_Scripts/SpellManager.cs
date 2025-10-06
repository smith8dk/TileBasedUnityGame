// SpellManager.cs
using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Tilemaps;
using UnityEngine.EventSystems;

public class SpellManager : MonoBehaviour
{
    AudioManager audioManager;
    private InputHandler inputHandler;
    private PlayerInput playerInput;

    [Header("Spawn Settings")]
    public float spawnDistance = 1f;

    private GameObject spellInstance;    // Prefab to shoot
    [SerializeField] private GameObject hitboxPrefab;
    private GameObject hitboxInstance;   // Visual aiming preview

    public bool aiming = false;
    public bool unique = false;

    // multiplier passed from UI (default 1.0)
    private float pendingDamageMultiplier = 1f;

    // optional: what draggable GameObject initiated this shot (used for cooldown)
    private GameObject pendingSourceDraggable = null;
    private int pendingCooldownTurns = 0;

    private GameObject playerObject;

    [Header("Refs")]
    [SerializeField] private UIHandler uiHandler;
    [SerializeField] private PlayerController playerController;
    [SerializeField] private TileHoverHighlighter tileHoverHighlighter;

    private void Awake()
    {
        audioManager = GameObject.FindGameObjectWithTag("Audio")
                       ?.GetComponent<AudioManager>();
        if (tileHoverHighlighter == null)
            tileHoverHighlighter = FindObjectOfType<TileHoverHighlighter>();
    }

    private void Start()
    {
        playerObject = GameObject.FindWithTag("Player");
        inputHandler = playerObject.GetComponent<InputHandler>();
        playerInput   = playerObject.GetComponent<PlayerInput>();

        // Unique spell clicks
        tileHoverHighlighter.OnValidTileClick += HandleValidTileClick;
        // Non-unique clicks
        playerInput.actions["Click"].performed += OnMouseClick;
    }

    private void OnDestroy()
    {
        if (tileHoverHighlighter != null)
            tileHoverHighlighter.OnValidTileClick -= HandleValidTileClick;

        // Safely unsubscribe from the click action
        if (playerInput != null && playerInput.actions != null)
        {
            var clickAction = playerInput.actions.FindAction("Click");
            if (clickAction != null)
                clickAction.performed -= OnMouseClick;
        }
    }


    private void Update()
    {
        // If we're aiming and have a hitbox preview AND it's not a unique (tile-click) spell,
        // snap the hitbox either in 4-way or 8-way depending on the currently selected spell prefab.
        if (aiming && hitboxInstance != null && !unique)
        {
            // Use 8-way snapping when the spell prefab name contains "Rebound"
            bool useEightWay = spellInstance != null && spellInstance.name.Contains("Rebound");
            SnapHitboxToDirection(useEightWay);
        }

        if (!aiming && hitboxInstance != null)
            hitboxInstance.SetActive(false);

        if (aiming && inputHandler.ExitKeyPressed())
            CancelAiming();
    }

    /// <summary>
    /// Starts the aiming workflow. If sourceDraggable is provided, its DraggableCooldown.defaultCooldownTurns (if present)
    /// will be used later when the spell actually spawns.
    /// </summary>
    public void TakeAim(GameObject spellPrefab, GameObject hitboxPrefabParam, float damageMultiplier = 1f, GameObject sourceDraggable = null)
    {
        if (aiming) return;

        // store requested multiplier for the next spawned spell
        pendingDamageMultiplier = Mathf.Max(0f, damageMultiplier);

        // store source draggable + determine default cooldown turns (if component present)
        pendingSourceDraggable = sourceDraggable;
        pendingCooldownTurns = 0;
        if (sourceDraggable != null)
        {
            var cd = sourceDraggable.GetComponentInChildren<DraggableCooldown>();
            if (cd != null) pendingCooldownTurns = Mathf.Max(0, cd.defaultCooldownTurns);
        }

        uiHandler.ToggleUI();
        aiming = true;
        spellInstance = spellPrefab;
        hitboxPrefab  = hitboxPrefabParam;
        inputHandler?.SwitchActionMap(new InputAction.CallbackContext());

        // Determine if this is a unique (tile-click) spell
        unique = hitboxPrefabParam.name.Contains("Lightning");

        StartCoroutine(DelayedSpawn());
    }

    private IEnumerator DelayedSpawn()
    {
        yield return new WaitForSeconds(0.2f);

        Vector3 spawnPos = playerObject.transform.position
                         + playerObject.transform.forward * spawnDistance;

        if (unique)
            tileHoverHighlighter.EnableHighlighting();

        hitboxInstance = Instantiate(hitboxPrefab, spawnPos, Quaternion.identity);
        hitboxInstance.SetActive(true);
    }

    private void HandleValidTileClick(Vector3Int cellPos)
    {
        if (!aiming || !unique) return;

        // 1) Turn off hover & UI
        tileHoverHighlighter.DisableHighlighting();

        // 2) Play SFX & player anim
        audioManager.PlaySFX(audioManager.attack);
        TriggerPlayerAnimation();

        // 3) Activate Enemy Turn
        playerController.ActivateEnemyTurn();

        // 4) Spawn the spell at tile (DelayedSpawnAtTile) — this will start cooldown
        StartCoroutine(DelayedSpawnAtTile(cellPos));

        // 5) Cleanup state
        CancelAiming();
    }

    private IEnumerator DelayedSpawnAtTile(Vector3Int cellPos)
    {
        yield return new WaitForSeconds(0.4f);

        // Grab the Tilemap component off the highlighter GameObject
        var tm = tileHoverHighlighter.GetComponent<Tilemap>();
        if (tm == null)
        {
            Debug.LogError("Tilemap component missing on TileHoverHighlighter GameObject!");
            yield break;
        }

        // Get exact world‐center of the clicked cell
        Vector3 worldPos = tm.GetCellCenterWorld(cellPos);

        // Instantiate and initialize
        GameObject go = Instantiate(spellInstance, worldPos, Quaternion.identity);

        var sp = go.GetComponent<Spell>();
        if (sp != null && pendingDamageMultiplier != 1f)
        {
            sp.damageAmount = Mathf.CeilToInt(sp.damageAmount * pendingDamageMultiplier);
        }

        // Initialize spell (owner still playerObject for collision ignore)
        go.GetComponent<Spell>()?.Initialize(Vector3.zero, playerObject);

        // Start cooldown on source draggable (only when the spell actually spawns)
        if (pendingSourceDraggable != null && pendingCooldownTurns > 0)
        {
            CooldownManager.Instance?.StartCooldownOn(pendingSourceDraggable, pendingCooldownTurns);
        }

        // Reset pending multiplier & source
        pendingDamageMultiplier = 1f;
        pendingSourceDraggable = null;
        pendingCooldownTurns = 0;

        // Advance turn already handled by caller ActivateEnemyTurn
    }

    private void OnMouseClick(InputAction.CallbackContext ctx)
    {
        // Existing logic for spells
        if (!aiming || unique) return;

        audioManager.PlaySFX(audioManager.attack);
        StartCoroutine(DelayedSpellSpawn());
        if (hitboxInstance != null)
            Destroy(hitboxInstance);
        aiming = false;
        unique = false;
        tileHoverHighlighter.DisableHighlighting();

        if (hitboxInstance != null)
            Destroy(hitboxInstance);
    }

    private IEnumerator DelayedSpellSpawn()
    {
        TriggerPlayerAnimation();
        yield return new WaitForSeconds(0.4f);
        
        inputHandler?.SwitchActionMapBack(new InputAction.CallbackContext());

        SpawnSpellInstance();
    }

    public void SpawnSpellInstance()
    {
        bool useEightWay = spellInstance != null && spellInstance.name.Contains("Rebound");
        Vector3 dir = useEightWay
            ? GetEightWayDirection(GlobalDirection.Direction)
            : GetFourWayDirection(GlobalDirection.Direction);

        Vector3 spawnPos = playerObject.transform.position + dir * spawnDistance;

        Debug.Log(dir);

        var newSpell = Instantiate(spellInstance, spawnPos, Quaternion.identity);
        var spellComp = newSpell.GetComponent<Spell>();
        if (spellComp != null && pendingDamageMultiplier != 1f)
        {
            spellComp.damageAmount = Mathf.CeilToInt(spellComp.damageAmount * pendingDamageMultiplier);
        }

        // Start cooldown on source draggable (only when the spell actually spawns)
        if (pendingSourceDraggable != null && pendingCooldownTurns > 0)
        {
            CooldownManager.Instance?.StartCooldownOn(pendingSourceDraggable, pendingCooldownTurns);
        }

        // reset pending multiplier & source
        pendingDamageMultiplier = 1f;
        pendingSourceDraggable = null;
        pendingCooldownTurns = 0;

        newSpell.GetComponent<Spell>()?.Initialize(dir, playerObject);

        playerController.ActivateEnemyTurn();
    }

    private void TriggerPlayerAnimation()
    {
        var anim = playerObject.GetComponent<Animator>();
        anim?.SetTrigger("Attack Charge");
    }

    public void CancelAiming()
    {
        aiming = false;
        unique = false;
        tileHoverHighlighter.DisableHighlighting();
        inputHandler?.SwitchActionMapBack(new InputAction.CallbackContext());

        if (hitboxInstance != null)
            Destroy(hitboxInstance);

        // clear pending source so cooldown won't be applied
        pendingSourceDraggable = null;
        pendingCooldownTurns = 0;
        pendingDamageMultiplier = 1f;
    }

    private void SnapHitboxToDirection(bool useEightWay)
    {
        Vector3 dir = (Camera.main.ScreenToWorldPoint(
                    Mouse.current.position.ReadValue())
                    - hitboxInstance.transform.position).normalized;

        GlobalDirection.Direction = useEightWay 
            ? GetEightWayDirection(dir) 
            : GetFourWayDirection(dir);

        // Rotate the hitbox visually to match snapped direction
        float angle = Mathf.Atan2(GlobalDirection.Direction.y, GlobalDirection.Direction.x) * Mathf.Rad2Deg;
        hitboxInstance.transform.rotation = Quaternion.Euler(0, 0, angle - 90f);
    }

    private Vector3 GetFourWayDirection(Vector3 d)
    {
        d.Normalize();
        float rawAngle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;

        // Snap to nearest 90°
        float a = Mathf.Round(rawAngle / 90f) * 90f;

        switch ((int)a)
        {
            case   0: return Vector3.right;
            case  90: return Vector3.up;
            case 180:
            case -180: return Vector3.left;
            case -90: return Vector3.down;
            default: return Vector3.right;  
        }
    }

    private Vector3 GetEightWayDirection(Vector3 d)
    {
        d.Normalize();
        float rawAngle = Mathf.Atan2(d.y, d.x) * Mathf.Rad2Deg;

        // Snap to nearest 45°
        float a = Mathf.Round(rawAngle / 45f) * 45f;

        switch ((int)a)
        {
            case   0:  return Vector3.right;                    
            case  45:  return new Vector3(1, 1, 0).normalized;  
            case  90:  return Vector3.up;                       
            case 135:  return new Vector3(-1, 1, 0).normalized; 
            case 180: 
            case -180: return Vector3.left;                     
            case -135: return new Vector3(-1, -1, 0).normalized;
            case -90:  return Vector3.down;                     
            case -45:  return new Vector3(1, -1, 0).normalized; 
            default:   return Vector3.right;
        }
    }
}
