using System.Collections;
using UnityEngine;
using UnityEngine.UI;

public class LoadingScreen : MonoBehaviour
{
    [Tooltip("CanvasGroup on the full-screen overlay (recommended)")]
    public CanvasGroup canvasGroup;

    [Tooltip("Optional: an Image component to tint or block (not required)")]
    public Image overlayImage;

    [Tooltip("Default fade duration (seconds)")]
    public float defaultFadeDuration = 0.35f;

    private void Reset()
    {
        // Try to auto-assign if user attached script in editor
        if (canvasGroup == null) canvasGroup = GetComponent<CanvasGroup>();
        if (overlayImage == null) overlayImage = GetComponent<Image>();
    }

    private void Awake()
    {
        if (canvasGroup == null)
            Debug.LogError("[LoadingScreen] canvasGroup not assigned.");
        // ensure hidden at start
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;
        gameObject.SetActive(false);
    }

    /// <summary> Fade the overlay in (blocks input while visible). </summary>
    public IEnumerator FadeIn(float duration = -1f)
    {
        if (duration <= 0f) duration = defaultFadeDuration;
        gameObject.SetActive(true);
        canvasGroup.blocksRaycasts = true;
        canvasGroup.interactable = true;

        float t = 0f;
        float start = canvasGroup.alpha;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime; // use unscaled so pauses don't affect it
            canvasGroup.alpha = Mathf.Lerp(start, 1f, t / duration);
            yield return null;
        }
        canvasGroup.alpha = 1f;
        yield break;
    }

    /// <summary> Fade the overlay out (unblocks input at end). </summary>
    public IEnumerator FadeOut(float duration = -1f)
    {
        if (duration <= 0f) duration = defaultFadeDuration;
        float t = 0f;
        float start = canvasGroup.alpha;
        while (t < duration)
        {
            t += Time.unscaledDeltaTime;
            canvasGroup.alpha = Mathf.Lerp(start, 0f, t / duration);
            yield return null;
        }
        canvasGroup.alpha = 0f;
        canvasGroup.blocksRaycasts = false;
        canvasGroup.interactable = false;
        gameObject.SetActive(false);
        yield break;
    }

    /// <summary>
    /// Helper: fade in, run the provided IEnumerator (work), then fade out.
    /// </summary>
    public IEnumerator ShowWhile(IEnumerator work, float fadeDuration = -1f)
    {
        yield return FadeIn(fadeDuration);
        // Give one frame to ensure overlay renders before heavy work
        yield return null;

        // Execute the work coroutine (if it's a blocking synchronous function,
        // wrap it in a coroutine that yields at least once or call it directly).
        if (work != null)
            yield return StartCoroutine(work);

        // ensure at least one frame so changes are visible
        yield return null;
        yield return FadeOut(fadeDuration);
    }
}
