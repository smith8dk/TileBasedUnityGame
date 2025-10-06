using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.Events; // Required for UnityEvent
using System.Collections;

public class WeaponClick : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] public GameObject attackPrefab; 
    [SerializeField] public GameObject hitboxPrefab;
    private SpellManager spellManager; // Reference to the SpellManager

    // Reference to the UIHandler
    [SerializeField] private UIHandler uiHandler;

    // Custom UnityEvent for when the object is clicked
    public UnityEvent onObjectClicked;

    private void Start()
    {
        // Find and cache the SpellManager
        spellManager = FindObjectOfType<SpellManager>();
        if (spellManager == null)
        {
            Debug.LogError("SpellManager not found in the scene.");
        }

        // Ensure the event is initialized
        if (onObjectClicked == null)
        {
            onObjectClicked = new UnityEvent();
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        // Ensure spellManager is not null before calling TakeAim
        if (spellManager != null)
        {
            spellManager.TakeAim(attackPrefab, hitboxPrefab); // Pass both instances to TakeAim
            // Invoke the custom event
            onObjectClicked.Invoke();
        }
        else
        {
            Debug.LogError("SpellManager reference is null.");
        }
        return;
    }
}
