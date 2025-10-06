// using UnityEngine;
// using UnityEngine.EventSystems;
// using UnityEngine.InputSystem;
// using UnityEngine.Events; // Required for UnityEvent
// using System.Collections;

// public class LightningClick : MonoBehaviour, IPointerClickHandler
// {
//     private SpellManager spellManager; // Reference to the SpellManager

//     // Reference to the UIHandler
//     [SerializeField] private UIHandler uiHandler;
//     [SerializeField] private GameObject hitboxInstance; // Instance of the hitbox object

//     // Custom UnityEvent for when the object is clicked
//     public UnityEvent onObjectClicked;

//     private void Start()
//     {
//         // Find and cache the SpellManager
//         spellManager = FindObjectOfType<SpellManager>();
//         if (spellManager == null)
//         {
//             Debug.LogError("SpellManager not found in the scene.");
//         }

//         // Ensure the event is initialized
//         if (onObjectClicked == null)
//         {
//             onObjectClicked = new UnityEvent();
//         }

//         // Add listener to invoke the UI toggle when clicked
//         //onObjectClicked.AddListener(uiHandler.ToggleUI);
//     }

//     public void OnPointerClick(PointerEventData eventData)
//     {
//         Debug.Log($"OnPointerClick called on {gameObject.name}, parent: {(transform.parent!=null?transform.parent.name:"<null>")}");
//         // Check if the object is a child of the object "Side-Menu"
//         if (transform.parent != null && transform.parent.name == "Side-Menu")
//         {
//             // Object is a child of "Side-Menu", so no action will be performed
//             Debug.Log("No action performed. Object is a child of Side-Menu.");
//             return;
//         }
//         else
//         {
//             Debug.LogWarning("else");
//             if (spellManager != null)
//             {
//                 Debug.LogWarning("spellinst");
//                 spellManager.TakeAim(hitboxInstance); // Pass instance
//                 // Invoke the custom event
//                 onObjectClicked.Invoke();
//             }
//             else
//             {
//                 Debug.LogError("SpellManager reference is null.");
//             }
//             return;
//         }
//     }
// }
