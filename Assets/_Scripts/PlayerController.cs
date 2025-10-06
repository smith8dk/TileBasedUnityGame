using UnityEngine;
using UnityEngine.InputSystem;
using System.Collections;

public class PlayerController : MonoBehaviour, IDamageable
{
    [Header("Player Stats")]
    [SerializeField] private int maxHealth = 100;

    private int currentHealth;
    [SerializeField] public float moveSpeed = 5f;
    public float defaultMoveSpeed;
    [SerializeField] private float detectionRadius = 10f;
    [SerializeField] private float sprintMultiplier = 2f;
    [SerializeField] private LayerMask whatStopsMovement;
    [SerializeField] private LayerMask exitTileInteraction;
    [SerializeField] private UIHandler uiHandler;

    [Header("Damage/Feedback")]
    [SerializeField] private float invulnerabilityDuration = 0.5f; // seconds of "i-frames"
    [SerializeField] private float flashDuration = 0.1f; // time per flash step
    private bool isInvulnerable = false;
    private SpriteRenderer spriteRenderer;

    [Header("Follow Point")]
    [Tooltip("Prefab for the follow point that lags one tile behind the move point.")]
    [SerializeField] private Transform followPointPrefab;

    public PlayerInput playerInput;
    private InputAction moveAction;
    private InputAction sprintAction;

    private Vector3Int currentTile;
    private Vector3Int targetTile;
    public Transform movePoint;

    private Transform followPoint;
    private Vector3 lastMovePointPosition;
    private bool pendingFollowUpdate = false;
    private PlayerHealthUI healthUI;
    private HealthBar healthBar;

    public CorridorFirstDungeonGenerator dungeonGenerator;
    private bool isSprinting = false;
    private int skipTurnQueued = 0;

    // NEW: track number of hearts applied so we can stack/unstack safely
    private int heartCount = 0;

    private void Awake()
    {
        currentHealth = maxHealth;
    }

    private void Start()
    {
        // auto-find the PlayerHealthUI in the scene
        healthUI = FindObjectOfType<PlayerHealthUI>();

        if (healthUI != null)
            healthUI.UpdateHealthDisplay(currentHealth, maxHealth);

        // auto-find HealthBar (slider)
        healthBar = FindObjectOfType<HealthBar>();
        if (healthBar != null)
            healthBar.UpdateHealthDisplay(currentHealth, maxHealth);
        defaultMoveSpeed = moveSpeed;

        // cache sprite renderer (for flash feedback)
        spriteRenderer = GetComponent<SpriteRenderer>();

        // Input setup
        playerInput = GetComponent<PlayerInput>();
        moveAction = playerInput.actions["Move"];
        sprintAction = playerInput.actions["Sprint"];

        // Tile tracking
        targetTile = currentTile = GetTilePosition(transform.position);

        // Ensure movePoint exists
        if (movePoint == null)
        {
            movePoint = new GameObject("MovePoint").transform;
            movePoint.position = transform.position;
        }

        // Instantiate the followPoint
        if (followPointPrefab != null)
        {
            followPoint = Instantiate(followPointPrefab);
            followPoint.name = "FollowPoint";
            followPoint.position = movePoint.position;
        }
        else
        {
            Debug.LogWarning("FollowPoint prefab not assigned on PlayerController.");
        }

        // Sprint callbacks
        sprintAction.started += OnSprintStarted;
        sprintAction.canceled += OnSprintCanceled;

        // Initialize UI if you want to show health at start
    }

    private void OnDestroy()
    {
        sprintAction.started -= OnSprintStarted;
        sprintAction.canceled -= OnSprintCanceled;
    }

    private void OnSprintStarted(InputAction.CallbackContext _) => isSprinting = true;
    private void OnSprintCanceled(InputAction.CallbackContext _) => isSprinting = false;

    public void SpawnPlayer(Vector2Int position)
    {
        playerInput.SwitchCurrentActionMap("Gameplay");
        Vector3 tileCenter = new Vector3(position.x + .5f, position.y + .5f, 0f);
        transform.position = tileCenter;
        currentTile        = GetTilePosition(tileCenter);
        movePoint.position = tileCenter;
        if (followPoint != null)
            followPoint.position = tileCenter;
    }

    private void Update()
    {
        // 1) Handle sprint: only when sprint key is down, no nearby enemies, and no spells moving
        float currentMoveSpeed = moveSpeed;
        if (isSprinting && !IsEnemyNearby() && !Spell.AnySpellsMoving())
            currentMoveSpeed *= sprintMultiplier;

        // 2) Check distance to the movePoint
        float dist = Vector3.Distance(transform.position, movePoint.position);

        // 3) Accept new input only if we're at the movePoint AND no enemies or spells are mid-move
        if (dist <= 0.05f && !Spell.AnySpellsMoving())
        {
            Vector2 input = moveAction.ReadValue<Vector2>();
            if (Mathf.Abs(input.x) == 1f || Mathf.Abs(input.y) == 1f)
            {
                Vector3 oldPos = movePoint.position;
                Vector3 delta  = new Vector3(input.x, input.y, 0f);

                if (!Physics2D.OverlapCircle(oldPos + delta, 0.2f, whatStopsMovement))
                {
                    // Trigger the turn system if there are enemies or spells active
                    bool enemyExists = EnemyController.activeEnemies.Count > 0;
                    bool spellExists = Spell.AnySpellsActive();

                    ActivateEnemyTurn();
                    if (uiHandler != null && uiHandler.MenuOpen)
                        uiHandler.RestrictMovement();

                    // Queue up the follow update and movePoint
                    lastMovePointPosition = oldPos;
                    pendingFollowUpdate   = true;
                    movePoint.position    = oldPos + delta;
                }

                // Exit‐tile check
                var cols = Physics2D.OverlapCircleAll(movePoint.position, 0.2f, exitTileInteraction);
                if (cols.Length > 0)
                    dungeonGenerator.GenerateDungeon();
            }
        }

        // 4) Smoothly move the player toward the movePoint
        transform.position = Vector3.MoveTowards(
            transform.position,
            movePoint.position,
            currentMoveSpeed * Time.deltaTime);

        // 5) Once arrived, update the followPoint
        if (pendingFollowUpdate && Vector3.Distance(transform.position, movePoint.position) <= 0.05f)
        {
            if (followPoint != null)
                followPoint.position = lastMovePointPosition;
            pendingFollowUpdate = false;
        }
    }

    public Vector2Int GetCurrentTilePosition()
    {
        Vector3Int t = GetTilePosition(transform.position);
        return new Vector2Int(t.x, t.y);
    }

    private bool IsEnemyNearby()
    {
        foreach (var e in EnemyController.activeEnemies)
            if (Vector3.Distance(transform.position, e.transform.position) <= detectionRadius)
                return true;
        return false;
    }
    private bool IsSpellNearby()
    {
        // look for any active Spell object within detectionRadius
        foreach (var go in GameObject.FindGameObjectsWithTag("Spell"))
        {
            if (Vector3.Distance(transform.position, go.transform.position) <= detectionRadius)
                return true;
        }
        return false;
    }

    private Vector3Int GetTilePosition(Vector3 pos)
    {
        return new Vector3Int(
            Mathf.RoundToInt(pos.x - .5f),
            Mathf.RoundToInt(pos.y - .5f),
            0);
    }

    // ----------------------------
    // IDamageable implementation
    // ----------------------------
    public void TakeDamage(int amount)
    {
        if (isInvulnerable) return; // ignore hits while invulnerable

        currentHealth = Mathf.Clamp(currentHealth - amount, 0, maxHealth);

        // update textual UI (if present)
        if (healthUI != null)
            healthUI.UpdateHealthDisplay(currentHealth, maxHealth);

        // update health bar UI (if present)
        if (healthBar != null)
            healthBar.UpdateHealthDisplay(currentHealth, maxHealth);

        // Visual feedback
        if (spriteRenderer != null)
            StartCoroutine(DamageFlashCoroutine());

        Debug.Log("Damage Taken: " + amount + " -> " + currentHealth);

        // If player died, handle it
        if (currentHealth <= 0)
            HandleDeath();
    }

    private IEnumerator DamageFlashCoroutine()
    {
        isInvulnerable = true;

        if (spriteRenderer != null)
        {
            Color original = spriteRenderer.color;
            // flash red briefly
            spriteRenderer.color = Color.red;
            yield return new WaitForSeconds(flashDuration);
            spriteRenderer.color = original;
        }

        // keep invulnerable for a small window
        yield return new WaitForSeconds(invulnerabilityDuration - flashDuration);
        isInvulnerable = false;
    }

    private void HandleDeath()
    {
        // Player Death Logic
        Debug.Log("Player died.");
        // TODO: call game over, reload, play death anim, etc.
    }

    /// <summary>
    /// Centralized turn activation: request enemies act or step spells.
    /// This method does NOT tick cooldowns — cooldown tick happens once in OnEnemyTurnComplete().
    /// </summary>
    public void ActivateEnemyTurn()
    {
        // Clean up any destroyed enemies so we don't call methods on them
        EnemyController.activeEnemies.RemoveAll(e => e == null);

        // If a SwordAttack hurtbox has detected a "Spell" collision this turn and it hasn't been consumed yet,
        // consume it and skip the enemies' turn. This only happens once per turn.
        if (SwordAttack.SpellCollisionThisTurn && !SwordAttack.SpellCollisionConsumedThisTurn)
        {
            SwordAttack.SpellCollisionConsumedThisTurn = true;
            Debug.Log("Enemy turn skipped due to SwordAttack <-> Spell collision.");
            // Treat the enemy turn as complete so player regains control.
            EnemyController.InvokeOnEnemyTurnComplete();
            return;
        }

        bool enemiesExist = EnemyController.activeEnemies.Count > 0;
        bool spellsActive = Spell.AnySpellsActive();

        if (IsEnemyNearby())
        {
            // block the player
            playerInput.SwitchCurrentActionMap("Restricted");

            // advance all enemies
            foreach (var enemy in EnemyController.activeEnemies)
                enemy.EnemyTurn();

            // spells also step when enemies act
            if (spellsActive)
                Spell.StepAllSpells();

            // enemy controllers will call OnEnemyTurnComplete when they finish
        }
        else if (enemiesExist)
        {
            // If enemies exist in the level but not in immediate detection, still run them
            foreach (var enemy in EnemyController.activeEnemies)
                enemy.EnemyTurn();

            if (spellsActive)
                Spell.StepAllSpells();

            // enemy controllers will call OnEnemyTurnComplete when they finish
        }
        else
        {
            // No enemies: still count as a turn — step spells and then signal completion.
            Spell.StepAllSpells();

            // Signal completion so PlayerController.OnEnemyTurnComplete runs and ticks cooldowns.
            EnemyController.InvokeOnEnemyTurnComplete();
        }
    }

    private void OnEnable()  => EnemyController.OnEnemyTurnComplete += OnEnemyTurnComplete;
    private void OnDisable() => EnemyController.OnEnemyTurnComplete -= OnEnemyTurnComplete;

    public void SkipTurn()
    {
        if (playerInput != null && playerInput.currentActionMap != null &&
            playerInput.currentActionMap.name == "Gameplay")
        {
            Debug.Log("[PlayerController] Player turn skipped immediately.");
            playerInput.SwitchCurrentActionMap("Restricted");
            ActivateEnemyTurn();
        }
        else
        {
            skipTurnQueued++;
            Debug.Log($"[PlayerController] SkipTurn queued. Total queued: {skipTurnQueued}");
        }
    }

    /// <summary>
    /// Called when the enemies (or the system) mark the enemy-turn as complete.
    /// This is the single place that ticks all draggable cooldowns exactly once per enemy-turn.
    /// </summary>
    private void OnEnemyTurnComplete()
    {
        // restore sword/spell flags
        SwordAttack.SpellCollisionThisTurn = false;
        SwordAttack.SpellCollisionConsumedThisTurn = false;

        // If we had queued skip-turns, consume one and immediately advance another enemy turn
        if (skipTurnQueued > 0)
        {
            skipTurnQueued--;
            Debug.Log($"[PlayerController] Queued SkipTurn activated. Remaining queued: {skipTurnQueued}");

            // Skip this turn: block player and advance enemies again (do NOT tick now)
            playerInput.SwitchCurrentActionMap("Restricted");
            ActivateEnemyTurn();
            return;
        }

        // Normal behavior: tick cooldowns once per enemy-turn completion BEFORE restoring player control
        try
        {
            CooldownManager.Instance?.TickAllTurns();
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning($"[PlayerController] CooldownManager.TickAllTurns() threw: {ex.Message}");
        }

        // Restore player control (ensure action map is Gameplay)
        if (playerInput.currentActionMap == null ||
            playerInput.currentActionMap.name != "Gameplay")
        {
            playerInput.SwitchCurrentActionMap("Gameplay");
        }
    }

    // -------------------
    // NEW: Heart handlers
    // -------------------

    /// <summary>
    /// Called when a Heart is placed into a radial menu slot (or restored into a slot).
    /// Behavior: increase maxHealth by 20, then also increase currentHealth by 20.
    /// Handles stacking (multiple hearts).
    /// </summary>
    public void OnHeartPlaced(GameObject heartGO)
    {
        if (heartGO == null) return;

        heartCount++;
        maxHealth += 20;

        // increase currentHealth by 20 (but clamp to new max)
        currentHealth = Mathf.Min(currentHealth + 20, maxHealth);

        // Update UI
        if (healthUI != null)
            healthUI.UpdateHealthDisplay(currentHealth, maxHealth);
        if (healthBar != null)
            healthBar.UpdateHealthDisplay(currentHealth, maxHealth);

        Debug.LogFormat("[PlayerController] Heart placed. heartCount={0} maxHealth={1} currentHealth={2}", heartCount, maxHealth, currentHealth);
    }

    /// <summary>
    /// Called when a Heart is removed from a radial menu slot (or moved to salvage).
    /// Behavior: decrease maxHealth by 20, and subtract 20 from currentHealth (clamped to >=0).
    /// </summary>
    public void OnHeartRemoved(GameObject heartGO)
    {
        if (heartGO == null) return;

        heartCount = Mathf.Max(0, heartCount - 1);
        maxHealth = Mathf.Max(0, maxHealth - 20);

        // subtract 20 from currentHealth; clamp >= 0 and <= maxHealth (if current > max, clamp down)
        currentHealth = Mathf.Max(0, currentHealth - 20);
        currentHealth = Mathf.Min(currentHealth, maxHealth);

        // Update UI
        if (healthUI != null)
            healthUI.UpdateHealthDisplay(currentHealth, maxHealth);
        if (healthBar != null)
            healthBar.UpdateHealthDisplay(currentHealth, maxHealth);

        Debug.LogFormat("[PlayerController] Heart removed. heartCount={0} maxHealth={1} currentHealth={2}", heartCount, maxHealth, currentHealth);
    }
}
