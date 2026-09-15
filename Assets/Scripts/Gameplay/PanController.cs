using System.Collections;
using UnityEngine;

/// <summary>
/// Triggers the pan flip and hands off the tossed waffle. Used as the
/// "sequence complete" beat: the pan tilts up, the waffle is tossed, and
/// both settle back to rest.
/// </summary>
public class PanController : MonoBehaviour
{
    private const float TossHeight = 2.2f;
    private const float TossDuration = 0.45f;
    private const float TiltAngle = 65f;

    [SerializeField] private float _tossHeight = TossHeight;
    [SerializeField] private float _tossDuration = TossDuration;
    [SerializeField] private float _tiltAngle = TiltAngle;

    private Quaternion _restRotation;
    private bool _hasRestRotation;

    private void Awake()
    {
        CacheRestRotation();
    }

    private void CacheRestRotation()
    {
        if (!_hasRestRotation)
        {
            _restRotation = transform.localRotation;
            _hasRestRotation = true;
        }
    }

    /// <summary>
    /// Flips the pan and tosses the supplied waffle. Spin count scales gently
    /// with accuracy so a cleaner run reads as a more confident flip.
    /// </summary>
    public IEnumerator Flip(WaffleController waffle, float accuracy)
    {
        CacheRestRotation();

        float spins = Mathf.Lerp(1f, 2f, Mathf.Clamp01(accuracy));

        // Tilt the pan up as the toss begins.
        Coroutine tilt = StartCoroutine(TiltRoutine());

        if (waffle != null)
        {
            yield return waffle.FlipToss(_tossHeight, _tossDuration, spins);
        }
        else
        {
            yield return new WaitForSeconds(_tossDuration);
        }

        if (tilt != null)
        {
            yield return tilt;
        }

        transform.localRotation = _restRotation;
    }

    private IEnumerator TiltRoutine()
    {
        float half = _tossDuration * 0.5f;
        Quaternion tilted = _restRotation * Quaternion.Euler(-_tiltAngle, 0f, 0f);

        yield return LerpRotation(_restRotation, tilted, half);
        yield return LerpRotation(tilted, _restRotation, half);
    }

    private IEnumerator LerpRotation(Quaternion from, Quaternion to, float duration)
    {
        if (duration <= 0f)
        {
            transform.localRotation = to;
            yield break;
        }

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            transform.localRotation = Quaternion.Slerp(from, to, t);
            yield return null;
        }

        transform.localRotation = to;
    }
}
