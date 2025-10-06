using UnityEngine;
using UnityEngine.InputSystem;

public class DungeonCameraCapture : MonoBehaviour
{
    [Header("Assign in Inspector")]
    public Camera mainCamera;      // Drag the main camera here in the inspector
    public Camera dungeonCamera;   // Drag the dungeon camera here in the inspector
    public GameObject player;      // Drag the Player object here in the inspector

    private PlayerInput playerInput;

    private void Awake()
    {
        // Optional: validate references early
        if (mainCamera == null)
            Debug.LogWarning("Main Camera not assigned in DungeonCameraCapture.");
        if (dungeonCamera == null)
            Debug.LogWarning("Dungeon Camera not assigned in DungeonCameraCapture.");
        if (player == null)
            Debug.LogWarning("Player GameObject not assigned in DungeonCameraCapture.");
    }

    private void Start()
    {
        // Get PlayerInput and register input callback
        if (player != null)
        {
            playerInput = player.GetComponent<PlayerInput>();
            if (playerInput != null)
            {
                // When "OpenMap" action is performed, toggle cameras
                playerInput.actions["OpenMap"].performed += OnOpenMapPerformed;
            }
            else
            {
                Debug.LogWarning("PlayerInput component not found on Player object in DungeonCameraCapture.");
            }
        }

        // Initialize: main camera active, dungeon camera inactive
        EnableMainCamera();
    }

    private void OnDestroy()
    {
        // Unsubscribe input callback to avoid leaks
        if (playerInput != null)
        {
            playerInput.actions["OpenMap"].performed -= OnOpenMapPerformed;
        }
    }

    // Callback for the input action
    private void OnOpenMapPerformed(InputAction.CallbackContext context)
    {
        ToggleCameras();
    }

    /// <summary>
    /// Enables the main camera and disables the dungeon camera.
    /// </summary>
    public void EnableMainCamera()
    {
        if (mainCamera != null) mainCamera.enabled = true;
        if (dungeonCamera != null) dungeonCamera.enabled = false;
        Debug.Log("Main Camera enabled, Dungeon Camera disabled.");
    }

    /// <summary>
    /// Enables the dungeon camera and disables the main camera.
    /// </summary>
    public void EnableDungeonCamera()
    {
        if (mainCamera != null) mainCamera.enabled = false;
        if (dungeonCamera != null) dungeonCamera.enabled = true;
        Debug.Log("Dungeon Camera enabled, Main Camera disabled.");
    }

    /// <summary>
    /// Toggles between main camera and dungeon camera.
    /// If main camera is active, switches to dungeon; otherwise switches to main.
    /// </summary>
    public void ToggleCameras()
    {
        if (mainCamera == null || dungeonCamera == null)
        {
            Debug.LogWarning("Cannot toggle cameras: one or both camera references are null.");
            return;
        }

        if (mainCamera.enabled)
        {
            EnableDungeonCamera();
        }
        else
        {
            EnableMainCamera();
        }
    }

    /// <summary>
    /// Returns true if main camera is currently active (enabled).
    /// </summary>
    public bool IsMainCameraActive()
    {
        return mainCamera != null && mainCamera.enabled;
    }

    /// <summary>
    /// Returns true if dungeon camera is currently active (enabled).
    /// </summary>
    public bool IsDungeonCameraActive()
    {
        return dungeonCamera != null && dungeonCamera.enabled;
    }

    /// <summary>
    /// Switches to a specific camera by name. 
    /// Accepts "main" or "dungeon" (case-insensitive). 
    /// </summary>
    public void SwitchToCamera(string cameraName)
    {
        if (string.Equals(cameraName, "main", System.StringComparison.OrdinalIgnoreCase))
        {
            EnableMainCamera();
        }
        else if (string.Equals(cameraName, "dungeon", System.StringComparison.OrdinalIgnoreCase))
        {
            EnableDungeonCamera();
        }
        else
        {
            Debug.LogWarning($"Invalid cameraName '{cameraName}' passed to SwitchToCamera. Use \"main\" or \"dungeon\".");
        }
    }
}
