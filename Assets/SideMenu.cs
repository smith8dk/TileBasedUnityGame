using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class SideMenu : MonoBehaviour, IDropHandler
{
    [SerializeField] private int InventorySize = 1;

    public void OnDrop(PointerEventData eventData)
    {
        if (eventData.pointerDrag != null && transform.childCount < InventorySize)
        {
            GameObject dropped = eventData.pointerDrag;
            Draggable draggableItem = dropped.GetComponent<Draggable>();

            if (draggableItem != null)
            {
                draggableItem.parentAfterDrag = transform;
                Debug.Log("Dropped on InventorySlot");
            }
        }
    }
}