using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>Runs the decorating rounds: pulls cooked pancakes off the wobbly stack, decorates them from sequence input, and serves them onto a stacked plate structure.</summary>
public class DecoratingManager : MonoBehaviour
{
    private const float IntroSeconds = 1.5f;
    private const float ResolveBeatSeconds = 0.35f;
    private const float PlatePopSeconds = 0.25f;
    private const float ServeSquashAmount = 0.2f;
    private const float StructureDepthStep = 0.04f;
    private const float CorrectPressJiggle = 0.45f;
    private const string MoveActionPath = "Player/Move";

    // Proportions of the scene's authored plate, so served plates match it.
    private static readonly Vector3 PlateRimPosition = new Vector3(0f, -0.05f, 0.1f);
    private static readonly Vector3 PlateRimScale = new Vector3(2.8f, 0.4f, 0.78f);
    private static readonly Vector3 PlateTopPosition = new Vector3(0f, 0.15f, -0.16f);
    private static readonly Vector3 PlateTopScale = new Vector3(2.45f, 0.4f, 0.62f);
    private static readonly Vector3 PancakeSeat = new Vector3(0f, 0.2f, -0.35f);
    private static readonly Quaternion PlateFacing = Quaternion.Euler(90f, 0f, 0f);

    [Header("Config & Systems")]
    [SerializeField] private GameConfig _config;
    [SerializeField] private InputActionAsset _inputActions;

    [Header("Presentation")]
    [SerializeField] private SequenceDisplay _sequenceDisplay;
    [SerializeField] private HUDController _hud;
    [SerializeField] private CameraShake _cameraShake;
    [SerializeField] private PancakeDecorator _decorator;

    [Header("Stations")]
    [Tooltip("Wobbly pile the cooked pancakes are rebuilt on and pulled from.")]
    [SerializeField] private WaffleStack _sourceStack;
    [Tooltip("Where each pancake is plated and decorated.")]
    [SerializeField] private Transform _stationAnchor;
    [Tooltip("Base of the structure the finished plates stack into.")]
    [SerializeField] private Transform _structureAnchor;

    [Header("Plates")]
    [SerializeField] private Material _plateMaterial;
    [SerializeField] private Material _plateRimMaterial;
    [Tooltip("Size of the decorating plate relative to the scene's big plate proportions.")]
    [SerializeField] private float _plateSize = 0.95f;

    [Header("Toss To Station")]
    [SerializeField] private float _tossHeight = 1.4f;
    [SerializeField] private float _tossDuration = 0.6f;
    [SerializeField] private float _tossSpins = 2f;

    [Header("Plate Structure")]
    [Tooltip("Scale of a finished plate once it joins the structure.")]
    [SerializeField] private float _structureScale = 0.38f;
    [Tooltip("Plates on the wide rows; the rows between hold one fewer, offset like a stack of cans.")]
    [SerializeField, Min(1)] private int _structureColumns = 2;
    [SerializeField] private float _structureColumnSpacing = 1.1f;
    [SerializeField] private float _structureRowHeight = 0.62f;
    [SerializeField] private float _serveHopHeight = 1.2f;
    [SerializeField] private float _serveDuration = 0.45f;

    [Header("Jiggle")]
    [Tooltip("Constant wobble of the pancake being decorated, in degrees.")]
    [SerializeField] private float _idleJiggleAngle = 2.5f;
    [Tooltip("Extra wobble at full energy (after a press or a mistake), in degrees.")]
    [SerializeField] private float _pressJiggleAngle = 10f;
    [SerializeField] private float _jiggleFrequency = 9f;
    [Tooltip("How quickly jiggle energy dies down per second.")]
    [SerializeField] private float _jiggleDecay = 2.5f;

    [Header("Testing")]
    [Tooltip("Golden pancakes to decorate when this scene is played directly without cooking first.")]
    [SerializeField] private int _fallbackPancakeCount = 6;

    private readonly SequenceGenerator _generator = new SequenceGenerator();
    private readonly AccuracyScorer _scorer = new AccuracyScorer();
    private readonly List<Direction> _enteredInputs = new List<Direction>();
    private readonly List<Transform> _servedPlates = new List<Transform>();

    private PlayerSession _session;
    private List<Direction> _currentTarget;
    private bool _capturingInput;
    private bool _hadWrongPress;
    private float _roundTimer;
    private bool _roundActive;

    // The plate currently being decorated and its wobbling pancake.
    private Transform _currentPlate;
    private Transform _currentJiggle;
    private Transform _currentDecorations;
    private PancakeFrame _currentFrame;
    private float _jiggleEnergy;

    private void Start()
    {
        SetupSession();
        FillSourceStack();
        StartCoroutine(RunDecorating());
    }

    private void Update()
    {
        TickRoundTimer();
        TickJiggle();
    }

    private void OnDestroy()
    {
        if (_session?.Input != null)
        {
            _session.Input.OnDirectionPressed -= OnDirectionPressed;
            _session.Input.Disable();
        }
    }

    private void SetupSession()
    {
        InputAction moveAction = null;
        if (_inputActions == null)
        {
            Debug.LogError("DecoratingManager: InputActionAsset is not assigned.");
        }
        else
        {
            moveAction = _inputActions.FindAction(MoveActionPath, false);
            if (moveAction == null)
            {
                Debug.LogError($"DecoratingManager: Could not find action '{MoveActionPath}'.");
            }
        }

        var reader = new DirectionalInputReader(moveAction);
        _session = new PlayerSession(0, reader);
        _session.CopyStatsFrom(PancakeCarryover.Session);
        reader.OnDirectionPressed += OnDirectionPressed;
        reader.Enable();
    }

    private void FillSourceStack()
    {
        if (_sourceStack == null || _config == null)
        {
            return;
        }

        if (!PancakeCarryover.HasData)
        {
            Debug.Log($"DecoratingManager: No pancakes carried over from cooking; using {_fallbackPancakeCount} test pancakes.");
            for (int i = 0; i < _fallbackPancakeCount; i++)
            {
                _sourceStack.PlaceWaffle(1f, _config, false);
            }

            return;
        }

        IReadOnlyList<PancakeRecord> pancakes = PancakeCarryover.Pancakes;
        for (int i = 0; i < pancakes.Count; i++)
        {
            _sourceStack.PlaceWaffle(pancakes[i].Accuracy, _config, pancakes[i].Burned);
        }
    }

    private IEnumerator RunDecorating()
    {
        if (_config == null || _sourceStack == null || _stationAnchor == null || _structureAnchor == null)
        {
            Debug.LogError("DecoratingManager: Config, source stack, station anchor, and structure anchor must all be assigned.");
            yield break;
        }

        _hud?.SetAccuracy(_session.DecorationAccuracy);
        UpdateDecoratedHud();
        yield return new WaitForSeconds(IntroSeconds);

        int firstRound = Mathf.Min(_config.CookingRoundCount, _config.RoundCount - 1);
        for (int round = firstRound; round < _config.RoundCount && _sourceStack.Count > 0; round++)
        {
            yield return RunRound(round);
        }

        _hud?.ShowResults(_session);
    }

    private IEnumerator RunRound(int roundIndex)
    {
        _roundTimer = _config.RoundDurationSeconds;
        _hud?.SetRound(roundIndex, _config.RoundCount);
        _hud?.SetTimer(_roundTimer);

        int sequenceLength = _config.GetSequenceLength(roundIndex);
        _roundActive = true;

        // Whatever is on the station when time runs out still gets served as-is.
        while (_roundTimer > 0f && _sourceStack.Count > 0)
        {
            yield return BringNextPancake();

            List<Direction> target = _generator.Generate(sequenceLength);
            _currentTarget = target;
            _enteredInputs.Clear();
            _hadWrongPress = false;

            if (_sequenceDisplay != null && _roundTimer > 0f)
            {
                yield return _sequenceDisplay.Play(target, _config.StepDisplaySeconds);
            }

            if (_roundTimer > 0f)
            {
                yield return CollectInput(target.Count);
            }

            yield return ResolveAndServe(target);
        }

        _roundActive = false;
    }

    private IEnumerator BringNextPancake()
    {
        GameObject pancake = _sourceStack.TakeTopWaffle();
        if (pancake == null)
        {
            yield break;
        }

        _currentPlate = new GameObject("DecoratedPancake").transform;
        _currentPlate.SetParent(_stationAnchor, false);
        Transform plate = BuildPlate(_currentPlate);
        StartCoroutine(JuiceTweens.PopIn(plate, plate.localScale, PlatePopSeconds));

        Transform pancakeTransform = pancake.transform;
        Vector3 landWorld = _currentPlate.TransformPoint(PancakeSeat * _plateSize);
        pancakeTransform.SetParent(_currentPlate, true);

        WaffleController waffle = pancake.GetComponent<WaffleController>();
        if (waffle != null)
        {
            yield return waffle.FlipToPlate(pancakeTransform.position, landWorld, _sourceStack.WaffleRotation, _tossHeight, _tossDuration, _tossSpins);
        }
        else
        {
            pancakeTransform.position = landWorld;
            pancakeTransform.rotation = _sourceStack.WaffleRotation;
        }

        MountOnJiggle(pancakeTransform);
    }

    private Transform BuildPlate(Transform parent)
    {
        Transform plate = new GameObject("Plate").transform;
        plate.SetParent(parent, false);
        plate.localScale = Vector3.one * _plateSize;

        Transform rim = PancakeDecorator.CreateVisual(PrimitiveType.Cylinder, "PlateRim", plate, _plateRimMaterial).transform;
        rim.localPosition = PlateRimPosition;
        rim.localRotation = PlateFacing;
        rim.localScale = PlateRimScale;

        Transform top = PancakeDecorator.CreateVisual(PrimitiveType.Cylinder, "PlateTop", plate, _plateMaterial).transform;
        top.localPosition = PlateTopPosition;
        top.localRotation = PlateFacing;
        top.localScale = PlateTopScale;

        return plate;
    }

    /// <summary>Re-parents the landed pancake under a pivot at its base so it can wobble and hold toppings.</summary>
    private void MountOnJiggle(Transform pancake)
    {
        Renderer pancakeRenderer = pancake.GetComponent<Renderer>();
        Bounds bounds = pancakeRenderer != null ? pancakeRenderer.bounds : new Bounds(pancake.position, new Vector3(2.3f, 0.6f, 0.8f));
        Vector3 center = _currentPlate.InverseTransformPoint(bounds.center);

        _currentJiggle = new GameObject("Jiggle").transform;
        _currentJiggle.SetParent(_currentPlate, false);
        _currentJiggle.localPosition = center - new Vector3(0f, bounds.extents.y, 0f);
        pancake.SetParent(_currentJiggle, true);

        _currentDecorations = new GameObject("Decorations").transform;
        _currentDecorations.SetParent(_currentJiggle, false);

        _currentFrame = new PancakeFrame(bounds.extents.x, bounds.extents.y * 2f, bounds.extents.z);
        _jiggleEnergy = 1f;
    }

    private IEnumerator CollectInput(int requiredCount)
    {
        _capturingInput = true;

        float window = _config.InputWindowSeconds;
        float elapsed = 0f;

        while (_capturingInput && _enteredInputs.Count < requiredCount && _roundTimer > 0f)
        {
            if (window > 0f)
            {
                elapsed += Time.deltaTime;
                if (elapsed >= window)
                {
                    break;
                }
            }

            yield return null;
        }

        _capturingInput = false;
    }

    private void OnDirectionPressed(Direction direction)
    {
        if (!_capturingInput || _currentTarget == null)
        {
            return;
        }

        int step = _enteredInputs.Count;
        if (step >= _currentTarget.Count)
        {
            return;
        }

        bool isWrong = _currentTarget[step] != direction;
        if (isWrong)
        {
            _hadWrongPress = true;
            _cameraShake?.Shake();
        }

        _enteredInputs.Add(direction);
        _sequenceDisplay?.ShowInputEcho(direction);
        _decorator?.AddPiece(_currentDecorations, _currentFrame, step, _currentTarget.Count, !isWrong);
        _jiggleEnergy = Mathf.Min(1f, _jiggleEnergy + (isWrong ? 1f : CorrectPressJiggle));
    }

    private IEnumerator ResolveAndServe(List<Direction> target)
    {
        if (_currentPlate == null)
        {
            yield break;
        }

        float accuracy = _scorer.Score(target, _enteredInputs, _config);
        bool perfect = !_hadWrongPress && _enteredInputs.Count == target.Count;

        if (_decorator != null)
        {
            if (perfect)
            {
                _decorator.AddCherry(_currentDecorations, _currentFrame);
            }
            else
            {
                _decorator.Jumble(_currentDecorations, Mathf.Lerp(0.35f, 1f, 1f - accuracy));
            }
        }

        _jiggleEnergy = 1f;
        _session.RecordDecoration(accuracy);
        _hud?.SetAccuracy(_session.DecorationAccuracy);

        yield return new WaitForSeconds(ResolveBeatSeconds);
        yield return ServeCurrentPlate();
    }

    private IEnumerator ServeCurrentPlate()
    {
        Transform plate = _currentPlate;
        _currentJiggle.localRotation = Quaternion.identity;
        _currentJiggle.localScale = Vector3.one;
        _currentPlate = null;
        _currentJiggle = null;
        _currentDecorations = null;
        UpdateDecoratedHud();

        Vector3 slotWorld = _structureAnchor.TransformPoint(StructureSlot(_servedPlates.Count));
        _servedPlates.Add(plate);
        plate.SetParent(_structureAnchor, true);

        Vector3 finalScale = Vector3.one * _structureScale;
        yield return JuiceTweens.Hop(plate, slotWorld, _serveHopHeight, _serveDuration, finalScale);
        yield return JuiceTweens.Squash(plate, finalScale, ServeSquashAmount);
    }

    /// <summary>Slot position (structure-local) for the nth plate: wide rows alternate with rows one plate narrower, like stacked cans.</summary>
    private Vector3 StructureSlot(int index)
    {
        int columns = Mathf.Max(1, _structureColumns);
        int row = 0;
        int slotsInRow = columns;

        while (index >= slotsInRow)
        {
            index -= slotsInRow;
            row++;
            slotsInRow = row % 2 == 0 ? columns : Mathf.Max(1, columns - 1);
        }

        float rowWidth = (slotsInRow - 1) * _structureColumnSpacing;
        float x = -rowWidth * 0.5f + index * _structureColumnSpacing;
        return new Vector3(x, row * _structureRowHeight, -row * StructureDepthStep);
    }

    // "Left" includes the pancake on the station until it has been served.
    private void UpdateDecoratedHud()
    {
        int remaining = _sourceStack.Count + (_currentPlate != null ? 1 : 0);
        _hud?.SetDecoratedCount(_session.DecoratedCount, remaining);
    }

    private void TickRoundTimer()
    {
        if (!_roundActive || _roundTimer <= 0f)
        {
            return;
        }

        _roundTimer = Mathf.Max(0f, _roundTimer - Time.deltaTime);
        if (_roundTimer <= 0f)
        {
            _roundActive = false;
        }

        _hud?.SetTimer(_roundTimer);
    }

    private void TickJiggle()
    {
        if (_currentJiggle == null)
        {
            return;
        }

        _jiggleEnergy = Mathf.MoveTowards(_jiggleEnergy, 0f, _jiggleDecay * Time.deltaTime);

        float phase = Time.time * _jiggleFrequency;
        float angle = Mathf.Sin(phase) * (_idleJiggleAngle + _jiggleEnergy * _pressJiggleAngle);
        float squash = Mathf.Sin(phase * 1.7f) * (0.015f + _jiggleEnergy * 0.06f);

        _currentJiggle.localRotation = Quaternion.Euler(0f, 0f, angle);
        _currentJiggle.localScale = new Vector3(1f + squash, 1f - squash, 1f + squash);
    }
}
