using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class PlayerHealthUI : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI healthText;

    public void UpdateHealthDisplay(int current, int max)
    {
        if (healthText != null)
        {
            healthText.text = $"{current}/{max}";
        }
    }
}
