using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

public class UIHandler : MonoBehaviour
{
    [SerializeField] private List<GameObject> uiObjects; // List of UI GameObjects to toggle
    PlayerInput playerInput;
    private InputHandler inputHandler; // Reference to InputHandler component on the player object
    private GameObject playerObject; // Reference to the player object
    public bool MenuOpen = false;
    [SerializeField] private SpellManager spellManager;
    [SerializeField] private DungeonCameraCapture dungeonCameraCapture;

    private GameObject FindPlayerObject()
    {
        GameObject playerObj = GameObject.FindGameObjectWithTag("Player"); // Adjust tag according to your setup

        if (playerObj == null)
        {
            Debug.LogError("Player object not found in the scene with tag 'Player'.");
        }

        return playerObj;
    }
    private void Start()
    {
        // Find and cache the player object
        playerObject = FindPlayerObject();
        // Ensure that all menu GameObjects are initially inactive
        foreach (GameObject uiObject in uiObjects)
        {
            if (uiObject != null)
            {
                uiObject.SetActive(false);
            }
            else
            {
                Debug.LogError("A UI GameObject reference is not set.");
            }
        }
        // Get the InputHandler component from the player object
        inputHandler = playerObject.GetComponent<InputHandler>();
        if (inputHandler == null)
        {
            Debug.LogError("InputHandler component not found on the player object.");
        }

        // Instantiate the PlayerInput component
        playerInput = GetComponent<PlayerInput>();
        
        // Subscribe to the "OpenMenu" action
        playerInput.actions["OpenMenu"].performed += _ => RestrictMovement();
    }

    public void RestrictMovement()
    {
        if (inputHandler != null && !MenuOpen)
        {
            ToggleUI();
        }
        else if (inputHandler != null && MenuOpen && !spellManager.aiming)
        {
            inputHandler.SwitchActionMapBack(new InputAction.CallbackContext());
            ToggleUI();
        }
        else {
            Debug.LogWarning("Can't close/open menu while aiming");
        }
    }

    public void Toggle()
    {
        // Toggle the active state of each UI object in the list
        foreach (GameObject uiObject in uiObjects)
        {
            if (uiObject != null)
            {
                uiObject.SetActive(!uiObject.activeSelf);
            }
            else
            {
                Debug.LogError("A UI GameObject reference is not set.");
            }
        }

        if (MenuOpen)
        {
            // Closing menu: only update flag, do NOT re-enable dungeon camera
            MenuOpen = false;
        }
        else
        {
            // Opening menu: enable main camera (and by implication disable dungeon camera)
            if (dungeonCameraCapture != null)
            {
                dungeonCameraCapture.EnableMainCamera();
            }
            else
            {
                Debug.LogWarning("DungeonCameraCapture reference is null in UIHandler.");
            }
            MenuOpen = true;
        }
    }

    public void ToggleUI()
    {
        // Toggle the active state of each UI object in the list
        foreach (GameObject uiObject in uiObjects)
        {
            if (uiObject != null)
            {
                uiObject.SetActive(!uiObject.activeSelf);
            }
            else
            {
                Debug.LogError("A UI GameObject reference is not set.");
            }
        }
        if (MenuOpen)
        {
            spellManager.CancelAiming();
            // Closing menu: switch action map back, do NOT re-enable dungeon camera
            MenuOpen = false;
            inputHandler.SwitchActionMapBack(new InputAction.CallbackContext());
        }
        else
        {
            spellManager.CancelAiming();
            // Opening menu: switch action map, enable main camera
            MenuOpen = true;
            inputHandler.SwitchActionMap(new InputAction.CallbackContext());

            if (dungeonCameraCapture != null)
            {
                dungeonCameraCapture.EnableMainCamera();
            }
            else
            {
                Debug.LogWarning("DungeonCameraCapture reference is null in UIHandler.");
            }
        }
    }
}
