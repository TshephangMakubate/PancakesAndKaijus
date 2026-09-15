using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Orchestrates the cooking match as a finite state machine driving the
/// 30s-per-round Simon-says cooking loop. With a <see cref="PancakeDecorator"/>
/// assigned, each pancake is also decorated in the pan as it cooks and every
/// cooking round plays here; otherwise the cooked pancakes are handed to the
/// decorating scene. Afterwards the pancakes move on to the serving round.
/// Per-player state lives in <see cref="PlayerSession"/>.
/// </summary>
public class GameManager : MonoBehaviour
{
    /// <summary>High-level match phases.</summary>
    public enum GameState
    {
        Intro,
        RoundStart,
        SequenceShow,
        PlayerInput,
        Resolve,
        RoundEnd,
        GameOver
    }

    private const float IntroSeconds = 1.5f;
    private const float ResolveSettleSeconds = 0.3f;
    private const float SceneTransitionSeconds = 1.5f;
    private const float PancakePlopSeconds = 0.2f;
    private const float RoundBannerSeconds = 1.1f;
    private const float GoBannerSeconds = 0.5f;
    private const float TimesUpSeconds = 1.2f;
    private const float ThrowAwaySpin = 720f;
    private const float ThrowAwaySeconds = 1.1f;
    private const string DecorateTransitionMessage = "Time to decorate!";
    private const string ServeTransitionMessage = "Time to serve!";
    private const string FinalRoundMessage = "Final Round!";
    private const string GoMessage = "GO!";
    private const string TimesUpMessage = "Time's up!";
    private const string MoveActionPath = "Player/Move";

    // Up, over the chef's shoulder, and into the background.
    private static readonly Vector3 ThrowAwayVelocity = new Vector3(-3f, 8f, 4f);

    [Header("Config & Systems")]
    [SerializeField] private GameConfig _config;
    [SerializeField] private InputActionAsset _inputActions;

    [Header("Presentation")]
    [SerializeField] private SequenceDisplay _sequenceDisplay;
    [SerializeField] private HUDController _hud;
    [SerializeField] private PanController _pan;
    [SerializeField] private WaffleController _waffle;
    [SerializeField] private WaffleStack _waffleStack;
    [SerializeField] private CameraShake _cameraShake;

    [Header("Decorate In Pan")]
    [Tooltip("Assign to top each pancake in the pan as it cooks and play every cooking round in this scene. Leave empty to hand off to the decorating scene.")]
    [SerializeField] private PancakeDecorator _decorator;

    private readonly SequenceGenerator _generator = new SequenceGenerator();
    private readonly AccuracyScorer _scorer = new AccuracyScorer();
    private readonly List<PlayerSession> _sessions = new List<PlayerSession>();
    private readonly List<PancakeRecord> _cookedPancakes = new List<PancakeRecord>();

    private GameState _state = GameState.Intro;
    private float _roundTimer;
    private int _roundIndex;

    // Input capture state for the current sequence.
    private readonly List<Direction> _enteredInputs = new List<Direction>();
    private bool _capturingInput;

    // The sequence the player is currently trying to copy, used to detect
    // wrong presses for feedback (screen shake) and burning the waffle.
    private List<Direction> _currentTarget;

    // True once any press in the current sequence missed the expected step.
    private bool _hadWrongPress;

    // When true, the round timer counts down every frame regardless of phase.
    private bool _roundActive;

    // The pancake sitting in the pan collecting toppings (decorate-in-pan mode).
    private GameObject _panPancake;
    private Transform _panToppings;
    private PancakeFrame _panFrame;

    /// <summary>Current state of the match FSM.</summary>
    public GameState State => _state;

    private bool DecoratesInPan => _decorator != null;

    private void Start()
    {
        PancakeCarryover.Clear();
        SetupSessions();
        StartCoroutine(RunMatch());
    }

    private void Update()
    {
        // The round timer runs continuously so it never pauses while a
        // sequence is being shown or a waffle is being resolved.
        if (!_roundActive || _roundTimer <= 0f)
        {
            return;
        }

        _roundTimer -= Time.deltaTime;
        if (_roundTimer <= 0f)
        {
            _roundTimer = 0f;
            _roundActive = false;
        }

        _hud?.SetTimer(_roundTimer);
    }

    private void OnDestroy()
    {
        for (int i = 0; i < _sessions.Count; i++)
        {
            PlayerSession session = _sessions[i];
            if (session?.Input != null)
            {
                session.Input.OnDirectionPressed -= OnDirectionPressed;
                session.Input.Disable();
            }
        }
    }

    private void SetupSessions()
    {
        _sessions.Clear();

        InputAction moveAction = ResolveMoveAction();
        var reader = new DirectionalInputReader(moveAction);
        var session = new PlayerSession(0, reader);
        reader.OnDirectionPressed += OnDirectionPressed;
        reader.Enable();
        _sessions.Add(session);
    }

    private InputAction ResolveMoveAction()
    {
        if (_inputActions == null)
        {
            Debug.LogError("GameManager: InputActionAsset is not assigned.");
            return null;
        }

        InputAction action = _inputActions.FindAction(MoveActionPath, false);
        if (action == null)
        {
            Debug.LogError($"GameManager: Could not find action '{MoveActionPath}'.");
        }

        return action;
    }

    private IEnumerator RunMatch()
    {
        if (_config == null)
        {
            Debug.LogError("GameManager: GameConfig is not assigned. Aborting match.");
            yield break;
        }

        _state = GameState.Intro;
        _hud?.SetAccuracy(0f);
        yield return new WaitForSeconds(IntroSeconds);

        int roundsHere = DecoratesInPan ? _config.RoundCount : _config.CookingRoundCount;
        for (_roundIndex = 0; _roundIndex < roundsHere; _roundIndex++)
        {
            yield return AnnounceRound(_roundIndex);
            yield return RunRound(_roundIndex);

            _hud?.ShowBanner(TimesUpMessage);
            yield return new WaitForSeconds(TimesUpSeconds);
            _hud?.HideMessage();
        }

        PlayerSession session = _sessions.Count > 0 ? _sessions[0] : null;

        if (!DecoratesInPan && _config.HasDecoratingRounds)
        {
            if (Application.CanStreamedLevelBeLoaded(_config.DecoratingSceneName))
            {
                yield return HandOffTo(_config.DecoratingSceneName, DecorateTransitionMessage, session);
                yield break;
            }

            Debug.LogError($"GameManager: '{_config.DecoratingSceneName}' is not in Build Settings. Run Waffle Party > Set Up Decorating Scene.");
        }

        if (_config.HasServingRound)
        {
            if (Application.CanStreamedLevelBeLoaded(_config.ServingSceneName))
            {
                yield return HandOffTo(_config.ServingSceneName, ServeTransitionMessage, session);
                yield break;
            }

            Debug.LogWarning($"GameManager: '{_config.ServingSceneName}' is not in Build Settings yet, so the match ends here. Run Waffle Party > Create Serving Scene.");
        }

        _state = GameState.GameOver;
        if (session != null)
        {
            _hud?.ShowResults(session);
        }
    }

    /// <summary>Carries the cooked pancakes and stats over and loads the next scene.</summary>
    private IEnumerator HandOffTo(string sceneName, string message, PlayerSession session)
    {
        PancakeCarryover.Store(session, _cookedPancakes);
        _hud?.ShowBanner(message);
        yield return new WaitForSeconds(SceneTransitionSeconds);
        SceneManager.LoadScene(sceneName);
    }

    /// <summary>Big "Round N" (or "Final Round!") banner then "GO!", before the round's clock starts.</summary>
    private IEnumerator AnnounceRound(int roundIndex)
    {
        _state = GameState.RoundStart;
        _hud?.SetRound(roundIndex, _config.DisplayedRoundCount);
        _hud?.SetTimer(_config.RoundDurationSeconds);

        bool finalRound = roundIndex == _config.DisplayedRoundCount - 1 && _config.DisplayedRoundCount > 1;
        _hud?.ShowBanner(finalRound ? FinalRoundMessage : $"Round {roundIndex + 1}");
        yield return new WaitForSeconds(RoundBannerSeconds);

        _hud?.ShowBanner(GoMessage);
        yield return new WaitForSeconds(GoBannerSeconds);
        _hud?.HideMessage();
    }

    private IEnumerator RunRound(int roundIndex)
    {
        _state = GameState.RoundStart;
        _roundTimer = _config.RoundDurationSeconds;

        PlayerSession session = _sessions.Count > 0 ? _sessions[0] : null;
        session?.BeginRound(roundIndex);

        _hud?.SetRound(roundIndex, _config.DisplayedRoundCount);
        _hud?.SetWaffleCount(session?.TotalWaffles ?? 0);
        _hud?.SetTimer(_roundTimer);

        int sequenceLength = _config.GetSequenceLength(roundIndex);

        _roundActive = true;

        while (_roundTimer > 0f)
        {
            if (DecoratesInPan)
            {
                yield return PreparePanPancake();
            }

            // SequenceShow. The round timer keeps ticking (see Update) so the
            // reveal counts against the round's clock, and time running out
            // cuts the reveal short.
            _state = GameState.SequenceShow;
            List<Direction> target = _generator.Generate(sequenceLength);
            _currentTarget = target;
            if (_sequenceDisplay != null && _roundTimer > 0f)
            {
                _sequenceDisplay.Begin(target, _config.StepDisplaySeconds);
                while (_sequenceDisplay.IsPlaying && _roundTimer > 0f)
                {
                    yield return null;
                }
            }

            if (_roundTimer <= 0f)
            {
                _sequenceDisplay?.Stop();
                break;
            }

            // PlayerInput.
            _state = GameState.PlayerInput;
            yield return CollectInput(target.Count);

            if (_roundTimer <= 0f)
            {
                break;
            }

            // Resolve.
            _state = GameState.Resolve;
            float accuracy = _scorer.Score(target, _enteredInputs, _config);

            // A single failed press burns the waffle regardless of overall score.
            bool burned = _hadWrongPress;

            // The waffle cooking in the pan reflects the latest bake.
            _waffle?.SetCharLevel(accuracy, _config, burned);

            if (DecoratesInPan)
            {
                FinishToppings(accuracy);
            }

            // Kick off the pan's flip gesture and the whimsical serve together.
            Coroutine panTilt = _pan != null ? StartCoroutine(_pan.Flip(null, accuracy)) : null;

            if (_waffleStack != null)
            {
                yield return _panPancake != null
                    ? _waffleStack.LaunchWaffle(_panPancake, accuracy, _config, burned)
                    : _waffleStack.ProduceWaffle(accuracy, _config, burned);
            }
            else if (_pan == null)
            {
                yield return new WaitForSeconds(ResolveSettleSeconds);
            }

            _panPancake = null;
            _panToppings = null;

            if (panTilt != null)
            {
                yield return panTilt;
            }

            _cookedPancakes.Add(new PancakeRecord(accuracy, burned));
            session?.RecordWaffle(accuracy, roundIndex);

            if (session != null)
            {
                _hud?.SetWaffleCount(session.TotalWaffles);
                _hud?.SetAccuracy(session.CumulativeAccuracy);
            }
        }

        // A pancake still in the pan when time runs out was never finished.
        ThrowAwayPanPancake();

        _roundActive = false;
        _state = GameState.RoundEnd;
    }

    /// <summary>Plops a raw pancake into the pan and readies it to collect toppings.</summary>
    private IEnumerator PreparePanPancake()
    {
        if (_waffleStack == null)
        {
            yield break;
        }

        _panPancake = _waffleStack.PrepareWaffle(_config);
        if (_panPancake == null)
        {
            yield break;
        }

        Transform pancake = _panPancake.transform;
        yield return JuiceTweens.PopIn(pancake, pancake.localScale, PancakePlopSeconds);

        if (_panPancake != null)
        {
            _panToppings = _decorator.CreateToppingsRoot(pancake, out _panFrame);
        }
    }

    private void FinishToppings(float accuracy)
    {
        if (_panToppings == null || _currentTarget == null)
        {
            return;
        }

        bool perfect = !_hadWrongPress && _enteredInputs.Count == _currentTarget.Count;
        if (perfect)
        {
            _decorator.AddCherry(_panToppings, _panFrame);
        }
        else
        {
            _decorator.Jumble(_panToppings, Mathf.Lerp(0.35f, 1f, 1f - accuracy));
        }
    }

    /// <summary>Tosses the unfinished pancake over the chef's shoulder; it doesn't count.</summary>
    private void ThrowAwayPanPancake()
    {
        if (_panPancake != null)
        {
            StartCoroutine(JuiceTweens.TossAway(_panPancake.transform, ThrowAwayVelocity, ThrowAwaySpin, ThrowAwaySeconds));
        }

        _panPancake = null;
        _panToppings = null;
    }

    private IEnumerator CollectInput(int requiredCount)
    {
        _enteredInputs.Clear();
        _hadWrongPress = false;
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
        // Gate input: ignore presses arriving outside the input phase.
        if (_state != GameState.PlayerInput || !_capturingInput)
        {
            return;
        }

        // A wrong press (doesn't match the expected step) shakes the screen.
        int stepIndex = _enteredInputs.Count;
        bool isWrong = _currentTarget == null
            || stepIndex >= _currentTarget.Count
            || _currentTarget[stepIndex] != direction;

        if (isWrong)
        {
            _hadWrongPress = true;
            _cameraShake?.Shake();
        }

        _enteredInputs.Add(direction);
        _sequenceDisplay?.ShowInputEcho(direction);

        if (DecoratesInPan && _panToppings != null && _currentTarget != null && stepIndex < _currentTarget.Count)
        {
            _decorator.AddPiece(_panToppings, _panFrame, stepIndex, _currentTarget.Count, !isWrong);

            // Any flubbed press scorches the pancake on the spot.
            WaffleController panWaffle = _panPancake != null ? _panPancake.GetComponent<WaffleController>() : null;
            if (isWrong && panWaffle != null)
            {
                panWaffle.SetCharLevel(0f, _config, true);
            }
        }
    }
}
