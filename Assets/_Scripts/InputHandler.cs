using UnityEngine;
using UnityEngine.InputSystem;

public class InputHandler : MonoBehaviour
{
    private Camera _mainCamera;
    private PlayerInput playerInput;
    public Transform playerTransform;

    private void Awake()
    {
        _mainCamera = Camera.main;
        playerInput = GetComponent<PlayerInput>();
    }

    private void OnEnable()
    {
        playerInput.actions["SwitchRestricted"].performed += SwitchActionMap;
        playerInput.actions["SwitchGameplay"].performed += SwitchActionMapBack;
    }

    private void OnDisable()
    {
        playerInput.actions["SwitchRestricted"].performed -= SwitchActionMap;
        playerInput.actions["SwitchGameplay"].performed -= SwitchActionMapBack;
    }

    public void SwitchActionMap(InputAction.CallbackContext context)
    {
        // Switch to Fireball Aiming
        playerInput.SwitchCurrentActionMap("Restricted");
        Debug.Log("Switched to Restricted");
    }

    public void SwitchActionMapBack(InputAction.CallbackContext context)
    {
        // Switch to Fireball Aiming
        playerInput.SwitchCurrentActionMap("Gameplay");
        Debug.Log("Switched to Gameplay");
    }
    public bool ExitKeyPressed()
    {
        return Keyboard.current.escapeKey.wasPressedThisFrame;
        // Or however you determine the "Exit" key in your input system
    }

}
