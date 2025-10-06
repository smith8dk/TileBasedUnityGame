using UnityEngine;
using UnityEngine.EventSystems;

public class Attack : MonoBehaviour, IPointerClickHandler
{
    private SpellManager spellManager;

    private void Start()
    {
        // Find the SpellManager in the scene
        spellManager = FindObjectOfType<SpellManager>();
        if (spellManager == null)
        {
            Debug.LogError("SpellManager not found in the scene.");
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (spellManager != null)
        {
            spellManager.SpawnSpellInstance();
        }
    }
}
