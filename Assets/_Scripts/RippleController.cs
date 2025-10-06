// using UnityEngine;

// public class RippleController : MonoBehaviour
// {
//     public static RippleController Instance;

//     [Header("Ripple Settings")]
//     public float rippleDuration = 1.5f;

//     private float rippleStartTime;
//     private Vector2 rippleOrigin;
//     private bool isRippling;

//     private void Awake()
//     {
//         if (Instance != null && Instance != this)
//         {
//             Destroy(gameObject);
//             return;
//         }
//         Instance = this;
//     }

//     void Update()
//     {
//         if (isRippling)
//         {
//             float elapsed = Time.time - rippleStartTime;
//             float progress = Mathf.Clamp01(elapsed / rippleDuration);

//             Shader.SetGlobalFloat("_RippleTime", progress);

//             if (progress >= 1f)
//                 isRippling = false;
//         }
//     }

//     public void TriggerRipple(Vector3 worldPosition)
//     {
//         Camera cam = Camera.main;
//         if (cam == null) return;

//         Vector3 viewportPos = cam.WorldToViewportPoint(worldPosition);
//         rippleOrigin = new Vector2(viewportPos.x, viewportPos.y);
//         rippleStartTime = Time.time;
//         isRippling = true;

//         Shader.SetGlobalVector("_RippleOrigin", rippleOrigin);
//         Shader.SetGlobalFloat("_RippleTime", 0f);
//     }
// }
