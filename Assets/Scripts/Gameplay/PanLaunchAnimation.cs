using UnityEngine;

/// <summary>
/// Decorative looping "pan toss" with a whimsical, arcade-y feel: the pan
/// coils back for a beat, whips the pancake into a high spinning arc with a
/// cheeky side-to-side wobble and a squash-and-stretch "boing", then both
/// bounce back to their resting pose with a little springy overshoot. Purely
/// cosmetic - it does not touch scoring or the gameplay loop, so it gives the
/// decorative (non-functional) station a lively launch that mirrors the
/// working station's gameplay toss.
/// </summary>
public class PanLaunchAnimation : MonoBehaviour
{
    private const float DefaultLaunchHeight = 2.2f;
    private const float DefaultCycleDuration = 1f;
    private const float DefaultAirborneFraction = 0.7f;
    private const float DefaultSpinsPerLaunch = 2f;
    private const float DefaultPanFlipAngle = 70f;
    private const float DefaultAnticipationFraction = 0.08f;
    private const float DefaultAnticipationDipAngle = 12f;
    private const float DefaultWobbleAngle = 14f;
    private const float DefaultSquashAmount = 0.22f;
    private const float DefaultLandingBounceFraction = 0.12f;

    [Tooltip("Pancake/waffle transform that gets tossed out of the pan.")]
    [SerializeField] private Transform _pancake;

    [Tooltip("Whole-pan transform that tilts up as the pancake launches (leave empty to skip the pan flip).")]
    [SerializeField] private Transform _panRoot;

    [Tooltip("Peak height of the toss above the resting position, in world units.")]
    [SerializeField] private float _launchHeight = DefaultLaunchHeight;

    [Tooltip("Seconds for one full toss-and-settle cycle (smaller = snappier, more arcade).")]
    [SerializeField] private float _cycleDuration = DefaultCycleDuration;

    [Tooltip("Fraction of the cycle the pancake spends airborne; the remainder is the coil-back and landing bounce.")]
    [SerializeField, Range(0.1f, 1f)] private float _airborneFraction = DefaultAirborneFraction;

    [Tooltip("Number of full flips the pancake performs during a single toss.")]
    [SerializeField] private float _spinsPerLaunch = DefaultSpinsPerLaunch;

    [Tooltip("Peak upward tilt of the whole pan during the launch, in degrees.")]
    [SerializeField] private float _panFlipAngle = DefaultPanFlipAngle;

    [Tooltip("Fraction of the cycle spent coiling back before the launch (an anticipation dip for a punchier toss).")]
    [SerializeField, Range(0f, 0.3f)] private float _anticipationFraction = DefaultAnticipationFraction;

    [Tooltip("How far the pan dips back during anticipation, in degrees.")]
    [SerializeField] private float _anticipationDipAngle = DefaultAnticipationDipAngle;

    [Tooltip("Cheeky side-to-side wobble the pancake does while spinning, in degrees.")]
    [SerializeField] private float _wobbleAngle = DefaultWobbleAngle;

    [Tooltip("How much the pancake squashes and stretches during the toss and landing bounce (0 = off).")]
    [SerializeField] private float _squashAmount = DefaultSquashAmount;

    [Tooltip("Fraction of the cycle spent on a springy landing bounce after the pancake lands.")]
    [SerializeField, Range(0f, 0.3f)] private float _landingBounceFraction = DefaultLandingBounceFraction;

    private Vector3 _restLocalPosition;
    private Quaternion _restLocalRotation;
    private Vector3 _restLocalScale;
    private Quaternion _panRestRotation;
    private bool _hasRest;

    private void OnEnable()
    {
        CacheRest();
    }

    private void OnDisable()
    {
        RestorePose();
    }

    /// <summary>Records the pancake and pan resting poses so each toss returns to them.</summary>
    private void CacheRest()
    {
        if (_hasRest)
        {
            return;
        }

        if (_pancake != null)
        {
            _restLocalPosition = _pancake.localPosition;
            _restLocalRotation = _pancake.localRotation;
            _restLocalScale = _pancake.localScale;
        }

        if (_panRoot != null)
        {
            _panRestRotation = _panRoot.localRotation;
        }

        _hasRest = _pancake != null;
    }

    /// <summary>Snaps the pancake and pan back to their cached resting poses.</summary>
    private void RestorePose()
    {
        if (!_hasRest)
        {
            return;
        }

        if (_pancake != null)
        {
            _pancake.localPosition = _restLocalPosition;
            _pancake.localRotation = _restLocalRotation;
            _pancake.localScale = _restLocalScale;
        }

        if (_panRoot != null)
        {
            _panRoot.localRotation = _panRestRotation;
        }
    }

    private void Update()
    {
        if (_pancake == null || _cycleDuration <= 0f)
        {
            return;
        }

        CacheRest();

        float cyclePhase = Mathf.Repeat(Time.time, _cycleDuration) / _cycleDuration;

        if (cyclePhase < _anticipationFraction)
        {
            AnimateAnticipation(cyclePhase / Mathf.Max(_anticipationFraction, 0.0001f));
            return;
        }

        if (cyclePhase < _airborneFraction)
        {
            float arc = (cyclePhase - _anticipationFraction) / Mathf.Max(_airborneFraction - _anticipationFraction, 0.0001f);
            AnimateLaunch(arc);
            return;
        }

        float landingEnd = Mathf.Min(_airborneFraction + _landingBounceFraction, 1f);
        if (cyclePhase < landingEnd)
        {
            float bounce = (cyclePhase - _airborneFraction) / Mathf.Max(landingEnd - _airborneFraction, 0.0001f);
            AnimateLandingBounce(bounce);
            return;
        }

        RestorePose();
    }

    /// <summary>Winds up before the toss: the pancake ducks and squashes wide while the pan dips back.</summary>
    private void AnimateAnticipation(float t)
    {
        float coil = Mathf.Sin(t * Mathf.PI * 0.5f);
        float dip = coil * (_launchHeight * 0.08f);
        float squash = 1f + coil * _squashAmount * 0.6f;

        _pancake.localPosition = _restLocalPosition + new Vector3(0f, -dip, 0f);
        _pancake.localRotation = _restLocalRotation;
        _pancake.localScale = new Vector3(_restLocalScale.x * squash, _restLocalScale.y / squash, _restLocalScale.z * squash);

        // The pan winds back opposite the launch tilt, like a chef cocking their wrist.
        if (_panRoot != null)
        {
            _panRoot.localRotation = _panRestRotation * Quaternion.Euler(coil * _anticipationDipAngle, 0f, 0f);
        }
    }

    /// <summary>The pancake shoots up on a parabolic arc with a spinning wobble and a puffy "boing"; the pan tips up under it.</summary>
    private void AnimateLaunch(float arc)
    {
        float lift = Mathf.Sin(arc * Mathf.PI);
        float height = lift * _launchHeight;
        float spin = arc * _spinsPerLaunch * 360f;
        float wobble = Mathf.Sin(arc * Mathf.PI * _spinsPerLaunch * 2f) * _wobbleAngle * lift;
        float pulse = 1f + lift * _squashAmount;

        _pancake.localPosition = _restLocalPosition + new Vector3(0f, height, 0f);
        _pancake.localRotation = _restLocalRotation * Quaternion.Euler(spin, 0f, wobble);
        _pancake.localScale = _restLocalScale * pulse;

        // The whole pan tips up fast under the toss and drops back with it.
        if (_panRoot != null)
        {
            _panRoot.localRotation = _panRestRotation * Quaternion.Euler(-lift * _panFlipAngle, 0f, 0f);
        }
    }

    /// <summary>A quick springy jiggle once the pancake lands, decaying back to a clean rest pose.</summary>
    private void AnimateLandingBounce(float t)
    {
        float damped = Mathf.Sin(t * Mathf.PI * 2.5f) * (1f - t) * (1f - t);
        float pulse = 1f + damped * _squashAmount;

        _pancake.localPosition = _restLocalPosition;
        _pancake.localRotation = _restLocalRotation;
        _pancake.localScale = _restLocalScale * pulse;

        if (_panRoot != null)
        {
            _panRoot.localRotation = _panRestRotation * Quaternion.Euler(-damped * _panFlipAngle * 0.2f, 0f, 0f);
        }
    }
}
