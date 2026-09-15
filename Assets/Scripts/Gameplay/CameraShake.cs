using System.Collections;
using UnityEngine;

/// <summary>
/// Applies a short, decaying positional shake to the camera. Trigger it with
/// <see cref="Shake"/> for punchy feedback such as a wrong button press.
/// </summary>
public class CameraShake : MonoBehaviour
{
    private const float DefaultDuration = 0.25f;
    private const float DefaultMagnitude = 0.35f;

    [Tooltip("Camera transform to shake. Defaults to this object's transform.")]
    [SerializeField] private Transform _target;

    private Vector3 _restLocalPosition;
    private Coroutine _shakeRoutine;

    private void Awake()
    {
        if (_target == null)
        {
            _target = transform;
        }

        _restLocalPosition = _target.localPosition;
    }

    /// <summary>
    /// Shakes the camera for <paramref name="duration"/> seconds with an
    /// initial <paramref name="magnitude"/> that eases out to zero.
    /// </summary>
    public void Shake(float duration = DefaultDuration, float magnitude = DefaultMagnitude)
    {
        if (_target == null)
        {
            return;
        }

        if (_shakeRoutine != null)
        {
            StopCoroutine(_shakeRoutine);
            _target.localPosition = _restLocalPosition;
        }

        _shakeRoutine = StartCoroutine(ShakeRoutine(duration, magnitude));
    }

    private IEnumerator ShakeRoutine(float duration, float magnitude)
    {
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;

            // Ease the shake out so it fades rather than stopping abruptly.
            float falloff = 1f - Mathf.Clamp01(elapsed / duration);
            float amount = magnitude * falloff;

            _target.localPosition = _restLocalPosition + new Vector3(
                Random.Range(-1f, 1f) * amount,
                Random.Range(-1f, 1f) * amount,
                0f);

            yield return null;
        }

        _target.localPosition = _restLocalPosition;
        _shakeRoutine = null;
    }
}
