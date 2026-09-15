using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Spawns a fresh waffle for every completed sequence, sends it on a whimsical
/// flip to the plate, and stacks the served waffles on top of one another.
/// Can also rebuild a carried-over pile and hand its top waffle off.
/// </summary>
public class WaffleStack : MonoBehaviour
{
    private const float FlipDuration = 0.55f;

    [Header("Flip Arc")]
    [Tooltip("Peak height of the toss arc; kept low so the apex stays in frame at the wide, low camera angle.")]
    [SerializeField] private float _flipHeight = 1.3f;

    [Header("Spawning")]
    [Tooltip("Waffle prefab spawned for each served waffle.")]
    [SerializeField] private GameObject _wafflePrefab;
    [Tooltip("Where the waffle launches from (the pan).")]
    [SerializeField] private Transform _panSpawnPoint;
    [Tooltip("Base landing point on the plate for the first waffle.")]
    [SerializeField] private Transform _plateAnchor;

    [Header("Look")]
    [Tooltip("Local scale applied to each spawned waffle (flat pancake).")]
    [SerializeField] private Vector3 _waffleScale = new Vector3(1.7f, 0.3f, 0.7f);
    [Tooltip("Rotation applied to each spawned waffle so it faces the camera.")]
    [SerializeField] private Vector3 _waffleEuler = new Vector3(90f, 0f, 0f);
    [Tooltip("Vertical spacing between stacked waffles.")]
    [SerializeField] private float _stackSpacing = 0.2f;
    [Tooltip("Depth step per waffle so higher layers render in front (no Z-fighting).")]
    [SerializeField] private float _depthStep = 0.03f;

    [Header("Flip Whimsy")]
    [SerializeField] private int _minSpins = 2;
    [SerializeField] private int _maxSpins = 4;
    [Tooltip("Batter puff played when a waffle launches from the pan.")]
    [SerializeField] private BatterSplash _splash;

    [Header("Leaning Pile")]
    [Tooltip("Horizontal drift added per stacked waffle (the lean direction).")]
    [SerializeField] private float _leanDrift = 0.045f;
    [Tooltip("Tilt in degrees added per stacked waffle.")]
    [SerializeField] private float _leanAngle = 1.6f;
    [Tooltip("Cap on a single waffle's resting tilt so tall piles lean without each waffle lying sideways.")]
    [SerializeField] private float _maxRestTilt = 8f;
    [Tooltip("Random horizontal jitter per waffle.")]
    [SerializeField] private float _positionJitter = 0.05f;
    [Tooltip("Random tilt jitter in degrees per waffle.")]
    [SerializeField] private float _tiltJitter = 2.5f;

    [Header("Wobble")]
    [Tooltip("Sway speed of a short stack; taller stacks sway slower and heavier.")]
    [SerializeField] private float _wobbleFrequency = 3.5f;
    [Tooltip("How much each extra waffle slows the sway (0 = constant speed).")]
    [SerializeField] private float _tallSwaySlowdown = 0.03f;
    [Tooltip("Bend in degrees each waffle adds on top of the one below it. Bends accumulate up the stack.")]
    [SerializeField] private float _bendPerLayer = 0.4f;
    [Tooltip("Extra bend per layer of height, so each higher waffle bends more than the last (0 = even bending).")]
    [SerializeField] private float _bendGrowthPerLayer = 0.15f;
    [Tooltip("Upper limit on the total sway at the top of the stack, in degrees.")]
    [SerializeField] private float _maxSwayAngle = 50f;
    [Tooltip("Phase delay per layer so the top whips after the base, like a noodle.")]
    [SerializeField] private float _layerPhaseLag = 0.35f;
    [Tooltip("Constant gentle idle sway as a fraction of the full sway.")]
    [SerializeField, Range(0f, 1f)] private float _idleWobble = 0.18f;
    [Tooltip("Wobble energy added each time a new waffle lands (0-1).")]
    [SerializeField] private float _landWobbleImpulse = 0.8f;
    [Tooltip("How quickly the landing wobble energy dies down per second.")]
    [SerializeField] private float _wobbleDecay = 1.6f;

    /// <summary>A single stacked waffle plus the rest pose the wobble sways around.</summary>
    private struct StackedWaffle
    {
        public GameObject Instance;
        public Vector3 RestPosition;
        public Quaternion RestRotation;
    }

    private readonly List<StackedWaffle> _served = new List<StackedWaffle>();
    private float _wobbleEnergy;
    private float _wobblePhase;

    /// <summary>Number of waffles currently stacked on the plate.</summary>
    public int Count => _served.Count;

    /// <summary>Upright orientation of a waffle lying on a plate.</summary>
    public Quaternion WaffleRotation => Quaternion.Euler(_waffleEuler);

    /// <summary>Resting (un-swayed) position of the top waffle, or the plate anchor when the stack is empty.</summary>
    public Vector3 TopRestPosition
    {
        get
        {
            if (_served.Count > 0)
            {
                return _served[_served.Count - 1].RestPosition;
            }

            return _plateAnchor != null ? _plateAnchor.position : transform.position;
        }
    }

    private void Update()
    {
        if (_served.Count == 0 || _plateAnchor == null)
        {
            return;
        }

        // Landing energy bleeds off so the extra jolt settles into idle sway.
        _wobbleEnergy = Mathf.MoveTowards(_wobbleEnergy, 0f, _wobbleDecay * Time.deltaTime);

        // Accumulated phase so the speed can change with height without jumping.
        _wobblePhase += Time.deltaTime * _wobbleFrequency / (1f + _served.Count * _tallSwaySlowdown);
        float energy = _idleWobble + _wobbleEnergy * (1f - _idleWobble);

        // Walk up the pile: each waffle bends a little on top of the one below,
        // so sway compounds with height and the top of a tall stack whips wildly.
        Vector3 previousRest = _plateAnchor.position;
        Vector3 previousPosition = previousRest;
        float sway = 0f;

        for (int i = 0; i < _served.Count; i++)
        {
            StackedWaffle waffle = _served[i];

            float bend = Mathf.Sin(_wobblePhase - i * _layerPhaseLag) * _bendPerLayer * (1f + i * _bendGrowthPerLayer) * energy;
            sway = Mathf.Clamp(sway + bend, -_maxSwayAngle, _maxSwayAngle);
            Quaternion swayRotation = Quaternion.Euler(0f, 0f, sway);

            Vector3 position = previousPosition + swayRotation * (waffle.RestPosition - previousRest);
            previousRest = waffle.RestPosition;
            previousPosition = position;

            if (waffle.Instance != null)
            {
                waffle.Instance.transform.position = position;
                waffle.Instance.transform.rotation = swayRotation * waffle.RestRotation;
            }
        }
    }

    /// <summary>
    /// Spawns, chars, and flips a new waffle onto the top of the plate stack.
    /// </summary>
    public IEnumerator ProduceWaffle(float accuracy, GameConfig config, bool burned = false)
    {
        GameObject instance = PrepareWaffle(config);
        if (instance == null)
        {
            yield break;
        }

        yield return LaunchWaffle(instance, accuracy, config, burned);
    }

    /// <summary>Spawns a raw waffle resting in the pan, ready to be topped and then launched.</summary>
    public GameObject PrepareWaffle(GameConfig config)
    {
        if (_wafflePrefab == null || _panSpawnPoint == null || _plateAnchor == null)
        {
            return null;
        }

        GameObject instance = SpawnWaffle(_panSpawnPoint.position);
        WaffleController waffle = instance.GetComponent<WaffleController>();
        waffle?.SetRaw(config);
        return instance;
    }

    /// <summary>Chars a prepared waffle and flips it from the pan onto the top of the stack.</summary>
    public IEnumerator LaunchWaffle(GameObject instance, float accuracy, GameConfig config, bool burned)
    {
        if (instance == null || _panSpawnPoint == null || _plateAnchor == null)
        {
            yield break;
        }

        WaffleController waffle = instance.GetComponent<WaffleController>();
        waffle?.SetCharLevel(accuracy, config, burned);

        // A puff of batter kicks up as the waffle launches from the pan.
        _splash?.Play(_panSpawnPoint.position);

        ComputeLanding(out Vector3 landPosition, out Quaternion landRotation);

        int spinRange = Mathf.Max(_minSpins, _maxSpins);
        float spins = Random.Range(_minSpins, spinRange + 1) + accuracy;

        if (waffle != null)
        {
            yield return waffle.FlipToPlate(instance.transform.position, landPosition, landRotation, _flipHeight, FlipDuration, spins);
        }
        else
        {
            instance.transform.position = landPosition;
            instance.transform.rotation = landRotation;
        }

        AddToStack(instance, landPosition, landRotation);
    }

    /// <summary>Drops an already-cooked waffle straight onto the pile with no toss.</summary>
    public void PlaceWaffle(float accuracy, GameConfig config, bool burned)
    {
        if (_wafflePrefab == null || _plateAnchor == null)
        {
            return;
        }

        ComputeLanding(out Vector3 landPosition, out Quaternion landRotation);
        GameObject instance = SpawnWaffle(landPosition);
        instance.transform.rotation = landRotation;

        WaffleController waffle = instance.GetComponent<WaffleController>();
        waffle?.SetCharLevel(accuracy, config, burned);

        AddToStack(instance, landPosition, landRotation);
    }

    /// <summary>Lifts the top waffle off the pile and returns it (null if empty); the rest of the pile jolts.</summary>
    public GameObject TakeTopWaffle()
    {
        if (_served.Count == 0)
        {
            return null;
        }

        int top = _served.Count - 1;
        GameObject instance = _served[top].Instance;
        _served.RemoveAt(top);
        _wobbleEnergy = Mathf.Min(1f, _wobbleEnergy + _landWobbleImpulse * 0.5f);
        return instance;
    }

    /// <summary>Clears the plate, destroying all stacked waffles.</summary>
    public void ResetStack()
    {
        for (int i = 0; i < _served.Count; i++)
        {
            if (_served[i].Instance != null)
            {
                Destroy(_served[i].Instance);
            }
        }

        _served.Clear();
        _wobbleEnergy = 0f;
    }

    private GameObject SpawnWaffle(Vector3 position)
    {
        GameObject instance = Instantiate(_wafflePrefab, transform);
        instance.transform.position = position;
        instance.transform.localScale = _waffleScale;
        instance.transform.rotation = Quaternion.Euler(_waffleEuler);
        return instance;
    }

    private void ComputeLanding(out Vector3 landPosition, out Quaternion landRotation)
    {
        // The pile keeps rising and drifting sideways; each waffle's own tilt is
        // capped so a tall stack leans as a whole rather than waffle by waffle.
        int layer = _served.Count;
        float lean = layer * _leanDrift + Random.Range(-_positionJitter, _positionJitter);
        float tilt = Mathf.Min(layer * _leanAngle, _maxRestTilt) + Random.Range(-_tiltJitter, _tiltJitter);

        landPosition = _plateAnchor.position;
        landPosition.y += _stackSpacing * layer;
        landPosition.x += lean;
        // Higher waffles render clearly in front so the stack never Z-fights.
        landPosition.z = _plateAnchor.position.z - (layer * _depthStep);

        landRotation = Quaternion.Euler(_waffleEuler.x, _waffleEuler.y, _waffleEuler.z + tilt);
    }

    private void AddToStack(GameObject instance, Vector3 restPosition, Quaternion restRotation)
    {
        // Record the settled pose so the wobble can sway around it, and jolt
        // the whole stack as the fresh waffle lands.
        _served.Add(new StackedWaffle
        {
            Instance = instance,
            RestPosition = restPosition,
            RestRotation = restRotation
        });
        _wobbleEnergy = Mathf.Min(1f, _wobbleEnergy + _landWobbleImpulse);
    }
}
