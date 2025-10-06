using System.Collections;
using UnityEngine;
using UnityEngine.InputSystem;

public class ShockWaveManager : MonoBehaviour
{
    [Header("ShockWave Settings")]
    [Tooltip("How long the shockwave animation runs")]
    [SerializeField] private float _shockWaveTime = 0.75f;

    [Tooltip("If you want to override the SpriteRenderer’s material\notherwise it'll grab it automatically")]
    [SerializeField] private Material _shockwaveMaterial;

    // caches
    private Coroutine _shockWaveCoroutine;
    private static readonly int _waveDistanceFromCenter = Shader.PropertyToID("_WaveDistanceFromCenter");

    private void Awake()
    {
        // grab the material if none assigned in inspector
        if (_shockwaveMaterial == null)
        {
            var sr = GetComponent<SpriteRenderer>();
            if (sr == null)
            {
                Debug.LogError("[ShockWaveManager] No SpriteRenderer found!");
            }
            else
            {
                _shockwaveMaterial = sr.material;
            }
        }
    }

    private void Update()
    {
        if (Keyboard.current.eKey.wasPressedThisFrame)
        {
            CallShockwave();
        }
    }

    private void CallShockwave()
    {
        // if one is already running, stop it so it restarts cleanly
        if (_shockWaveCoroutine != null)
            StopCoroutine(_shockWaveCoroutine);

        // start a brand‐new shockwave
        _shockWaveCoroutine = StartCoroutine(ShockWaveAction(-0.1f, 1f));
    }

    private IEnumerator ShockWaveAction(float startPos, float endPos)
    {
        // set initial distance
        _shockwaveMaterial.SetFloat(_waveDistanceFromCenter, startPos);

        float elapsedTime = 0f;
        while (elapsedTime < _shockWaveTime)
        {
            // advance timer
            elapsedTime += Time.deltaTime;

            // lerp 0→1 over duration
            float t = elapsedTime / _shockWaveTime;
            float lerped = Mathf.Lerp(startPos, endPos, t);

            // push to shader
            _shockwaveMaterial.SetFloat(_waveDistanceFromCenter, lerped);

            yield return null;
        }

        // ensure it ends at exactly endPos
        _shockwaveMaterial.SetFloat(_waveDistanceFromCenter, endPos);

        // clear the reference
        _shockWaveCoroutine = null;
    }
}
