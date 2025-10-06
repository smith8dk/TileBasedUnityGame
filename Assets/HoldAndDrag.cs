using UnityEngine;
using UnityEngine.EventSystems;
using System;

public class DragAndHold : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
{
    [SerializeField] private float holdTime = 1f; // Time to hold before starting drag
    private float holdTimer = 0f;
    private bool isHolding = false;
    private bool isDragging = false;
    private Draggable draggable; // Reference to Draggable script on the child
    public static event Action OnItemDraggedOrDropped;

    private void Update()
    {
        // Dynamically check for Draggable component if child is added later
        if (transform.childCount > 0 && draggable == null)
        {
            Transform child = transform.GetChild(0); // Get the first child
            draggable = child.GetComponent<Draggable>();
        }

        // Timer to initiate drag
        if (isHolding)
        {
            holdTimer += Time.deltaTime;
            if (holdTimer >= holdTime && !isDragging)
            {
                StartDraggingChild(); // Start dragging the child object
            }
        }
    }

    public void OnPointerDown(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            isHolding = true; // Start the holding timer
            holdTimer = 0f;   // Reset the hold timer
        }
    }

    public void OnPointerUp(PointerEventData eventData)
    {
        if (eventData.button == PointerEventData.InputButton.Left)
        {
            isHolding = false;  // Stop holding
            isDragging = false; // End dragging
            holdTimer = 0f;     // Reset hold timer

            if (draggable != null)
            {
                draggable.OnEndDrag(eventData);  // Trigger the end of dragging

                // Trigger the OnItemDraggedOrDropped event after ending the drag (drop occurs)
                OnItemDraggedOrDropped?.Invoke();
                Debug.Log("OnItemDraggedOrDropped event triggered.");
            }
        }
    }

    private void StartDraggingChild()
    {
        if (draggable != null)
        {
            // Start dragging the child object
            isDragging = true;
            PointerEventData pointerEventData = new PointerEventData(EventSystem.current);
            draggable.OnBeginDrag(pointerEventData); // Trigger the beginning of the drag
        }
    }
}
