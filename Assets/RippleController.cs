using UnityEngine;
using UnityEngine.InputSystem;
using Pathfinding;
using System.Collections;
using System.Collections.Generic;
public class RippleController : MonoBehaviour
{
    public static RippleController Instance { get; private set; }

    // Maximum concurrent ripples
    [SerializeField] int maxRipples = 8;

    // Arrays of centers (in viewport UV) and spawn times
    Vector4[] rippleCenters;
    float[] rippleTimes;

    void Awake()
    {
        if (Instance != null) Destroy(gameObject);
        else { Instance = this; DontDestroyOnLoad(gameObject); }

        rippleCenters = new Vector4[maxRipples];
        rippleTimes = new float[maxRipples];
        for (int i = 0; i < maxRipples; i++)
            rippleTimes[i] = -9999f;  // mark as “unused”
    }

    /// <summary>
    /// Call this to spawn a ripple at world‑pos.
    /// </summary>
    public void SpawnRipple(Vector3 worldPos)
    {
        // convert world to viewport UV [0–1]
        Vector3 vp = Camera.main.WorldToViewportPoint(worldPos);
        Vector4 uv = new Vector4(vp.x, vp.y, 0, 0);

        float now = Time.time;
        // find first free slot (oldest or unused)
        int idx = 0;
        float oldest = rippleTimes[0];
        for (int i = 1; i < maxRipples; i++)
        {
            if (rippleTimes[i] < oldest)
            {
                oldest = rippleTimes[i];
                idx = i;
            }
        }

        rippleCenters[idx] = uv;
        rippleTimes[idx] = now;
    }

    void Update()
    {
        // each frame, push arrays into the global shader
        Shader.SetGlobalInt("_RippleCount", maxRipples);
        Shader.SetGlobalVectorArray("_RippleCenters", rippleCenters);
        Shader.SetGlobalFloatArray("_RippleTimes", rippleTimes);
        Shader.SetGlobalFloat("_GlobalRippleTime", Time.time);
    }
}
