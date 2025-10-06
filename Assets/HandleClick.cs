using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using System;
public class HandleClick : MonoBehaviour
{
    [SerializeField] private MenuHandler menuHandler;

    private void Start()
    {
        // Ensure menuHandler is assigned in the inspector
        if (menuHandler == null)
        {
            Debug.LogError("MenuHandler reference is missing.");
        }
    }

    public void OnButtonClick()
    {
        // Call the ToggleSlide method to slide the menu
        if (menuHandler != null)
        {
            menuHandler.ToggleSlide();
        }
    }
}
