using UnityEngine;
using UnityEngine.UI;

[RequireComponent(typeof(Slider))]
public class HealthBar : MonoBehaviour
{
    [Tooltip("How fast the slider visually catches up to the target value (seconds). 0 = instant.")]
    [SerializeField] private float smoothTime = 0.08f;

    private Slider slider;
    private float targetValue;
    private float velocity = 0f;

    private void Awake()
    {
        // Try to find a Slider on this GameObject or in its children
        slider = GetComponent<Slider>();
        if (slider == null)
            slider = GetComponentInChildren<Slider>();

        if (slider == null)
        {
            Debug.LogError("[HealthBar] No Slider component found on GameObject or children. Disabling HealthBar.");
            enabled = false;
            return;
        }

        // Initialize target to current slider value so it doesn't snap on first frame
        targetValue = slider.value;
    }

    private void Update()
    {
        // Smoothly approach the target value if requested
        if (slider != null)
        {
            if (smoothTime <= 0f)
            {
                slider.value = targetValue;
            }
            else
            {
                slider.value = Mathf.SmoothDamp(slider.value, targetValue, ref velocity, smoothTime);
            }
        }
    }

    /// <summary>
    /// Called externally (e.g. by PlayerController) to update the visual health bar.
    /// This method does NOT query the player directly — pass current and max health here.
    /// </summary>
    public void UpdateHealthDisplay(int current, int max)
    {
        if (slider == null) return;

        // Treat sliders with maxValue > 1 as absolute (0..max)
        if (slider.maxValue > 1.001f)
        {
            // Make sure slider's max matches player's max (keeps inspector-configured sliders in sync)
            slider.maxValue = Mathf.Max(1, max);
            // Set target to absolute clamped health value
            targetValue = Mathf.Clamp(current, 0, max);
        }
        else
        {
            // Normalized slider (0..1)
            float norm = (max > 0) ? (float)current / max : 0f;
            targetValue = Mathf.Clamp01(norm);
        }
    }

    /// <summary>
    /// Force the slider to instantly match the latest target (useful when teleporting/respawning).
    /// </summary>
    public void SnapToCurrent()
    {
        if (slider == null) return;
        slider.value = targetValue;
        velocity = 0f;
    }
}
