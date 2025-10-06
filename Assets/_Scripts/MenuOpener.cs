using UnityEngine;

public class MenuOpener : MonoBehaviour
{
    [SerializeField] private GameObject menu; // Reference to the menu GameObject

    private void Start()
    {
        // Ensure that the menu GameObject is initially inactive
        if (menu != null)
        {
            menu.SetActive(false);
        }
        else
        {
            Debug.LogError("Menu GameObject reference is not set.");
        }
    }

    private void Update()
    {
        // Check if the shift key is pressed
        if (Input.GetKeyDown(KeyCode.LeftShift) || Input.GetKeyDown(KeyCode.RightShift))
        {
            // Toggle the menu on/off
            ToggleMenu();
        }
    }

    private void ToggleMenu()
    {
        // Check if the menu reference is set
        if (menu != null)
        {
            // Toggle the menu active state
            menu.SetActive(!menu.activeSelf);
        }
        else
        {
            Debug.LogError("Menu GameObject reference is not set.");
        }
    }
}
