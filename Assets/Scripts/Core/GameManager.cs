using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

/// <summary>
/// Orchestrates the cooking match as a finite state machine driving the
/// 30s-per-round cooking loop. With a <see cref="PancakeDecorator"/>
/// assigned, each pancake is also decorated in the pan as it cooks and every
/// cooking round plays here; otherwise the cooked pancakes are handed to the
/// decorating scene. Afterwards the pancakes move on to the serving round.
/// <para>
/// Each pancake is now earned by completing one co-op token sequence on
/// <see cref="SequenceMatchRunner"/> rather than by copying a Simon-says
/// pattern. This manager still owns the pacing: it deals a sequence, keeps the
/// team on it until they land it, and plays the pan and stack beats.
/// </para>
/// </summary>
public class GameManager : MonoBehaviour
{
    /// <summary>How the team's current sequence ended.</summary>
    private enum SequenceOutcome
    {
        Pending,
        Completed,
        TimedOut
    }

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

    // The cooking scene is played by team A; team B's station is enabled from
    // SequenceConfig once four players are available.
    private const int CookingTeamIndex = 0;

    // Up, over the chef's shoulder, and into the background.
    private static readonly Vector3 ThrowAwayVelocity = new Vector3(-3f, 8f, 4f);

    [Header("Config & Systems")]
    [SerializeField] private GameConfig _config;
    [SerializeField] private InputActionAsset _inputActions;
    [Tooltip("Hosts the co-op token sequences. Leave its Auto Deal off so this manager controls the pacing.")]
    [SerializeField] private SequenceMatchRunner _sequenceRunner;

    [Header("Presentation")]
    [Tooltip("Legacy Simon-says arrow display. No longer driven - the token stations replaced it. Kept so existing scene wiring is not lost.")]
    [SerializeField] private SequenceDisplay _sequenceDisplay;
    [SerializeField] private HUDController _hud;
    [SerializeField] private PanController _pan;
    [SerializeField] private WaffleController _waffle;
    [SerializeField] private WaffleStack _waffleStack;
    [SerializeField] private CameraShake _cameraShake;

    [Header("Decorate In Pan")]
    [Tooltip("Assign to top each pancake in the pan as it cooks and play every cooking round in this scene. Leave empty to hand off to the decorating scene.")]
    [SerializeField] private PancakeDecorator _decorator;

    private readonly List<PlayerSession> _sessions = new List<PlayerSession>();
    private readonly List<PancakeRecord> _cookedPancakes = new List<PancakeRecord>();

    private GameState _state = GameState.Intro;
    private float _roundTimer;
    private int _roundIndex;

    // How the sequence in play ended, polled by the round loop.
    private SequenceOutcome _outcome = SequenceOutcome.Pending;

    // Wrong presses made on the pancake in the pan, across every attempt at its
    // sequence. This is what sets the bake quality.
    private int _wrongPresses;

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
        PushRoundTimerToStations();
    }

    /// <summary>
    /// Drives the bar on each television from the round's clock. The stations
    /// have no notion of a round, so the reading has to be pushed to them; what
    /// the bar shows is the time left to cook, not the time left on one row.
    /// </summary>
    private void PushRoundTimerToStations()
    {
        if (_sequenceRunner == null || _config == null || _config.RoundDurationSeconds <= 0f)
        {
            return;
        }

        _sequenceRunner.SetRoundTimer(_roundTimer, _config.RoundDurationSeconds);
    }

    private void OnDestroy()
    {
        if (_sequenceRunner != null)
        {
            _sequenceRunner.OnTeamSequenceComplete -= HandleSequenceComplete;
            _sequenceRunner.OnTeamTimeout -= HandleSequenceTimeout;
            _sequenceRunner.OnTeamTokenCompleted -= HandleTokenCompleted;
            _sequenceRunner.OnTeamWrongPress -= HandleWrongPress;
        }
    }

    /// <summary>
    /// Creates the session that records the cooking team's output. The team's
    /// two players share one record because everything downstream — the waffle
    /// stack, <see cref="PancakeCarryover"/> and the serving round — is scored
    /// per team, not per person.
    /// </summary>
    private void SetupSessions()
    {
        _sessions.Clear();
        _sessions.Add(new PlayerSession(0));

        if (_sequenceRunner == null)
        {
            Debug.LogError("GameManager: SequenceMatchRunner is not assigned; no input will reach the game.");
            return;
        }

        // The round clock is the only timer here: a row never times out and a
        // wrong press leaves the team on the token they missed.
        _sequenceRunner.UseRoundClockOnly();

        _sequenceRunner.OnTeamSequenceComplete += HandleSequenceComplete;
        _sequenceRunner.OnTeamTimeout += HandleSequenceTimeout;
        _sequenceRunner.OnTeamTokenCompleted += HandleTokenCompleted;
        _sequenceRunner.OnTeamWrongPress += HandleWrongPress;
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

            // A first-to-N match is over the moment a team hits the target.
            if (MatchDecided)
            {
                break;
            }
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

        // Show the bar full behind the banner rather than wherever the last
        // round left it.
        _roundTimer = _config.RoundDurationSeconds;
        PushRoundTimerToStations();

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
        PushRoundTimerToStations();

        _roundActive = true;
        _sequenceRunner?.StartRound();

        while (_roundTimer > 0f && !MatchDecided)
        {
            if (DecoratesInPan)
            {
                yield return PreparePanPancake();
            }

            if (_roundTimer <= 0f)
            {
                break;
            }

            // SequenceShow. The whole row is visible from the moment it is
            // dealt, so there is no playback to wait through — the round clock
            // is the pressure.
            _state = GameState.SequenceShow;
            _wrongPresses = 0;
            _sequenceRunner?.DealTo(CookingTeamIndex);

            // PlayerInput. The pancake in the pan belongs to this row until the
            // team lands it. A wrong press keeps them on the token they missed,
            // so the only thing that takes a pancake away from them is the
            // round clock running out (see Update).
            _state = GameState.PlayerInput;
            while (_roundTimer > 0f)
            {
                _outcome = SequenceOutcome.Pending;
                while (_outcome == SequenceOutcome.Pending && _roundTimer > 0f)
                {
                    yield return null;
                }

                if (_outcome != SequenceOutcome.TimedOut)
                {
                    break;
                }

                // Only reachable if the row clock was turned back on: put the
                // same row back rather than hand the pancake a free pass.
                _sequenceRunner?.RedealTo(CookingTeamIndex);
            }

            if (_roundTimer <= 0f)
            {
                break;
            }

            // Resolve.
            _state = GameState.Resolve;
            bool ruined = _wrongPresses >= _config.MistakesBeforeRuined;
            float accuracy = BakeQuality(_wrongPresses);

            // The waffle cooking in the pan reflects the latest bake.
            _waffle?.SetCharLevel(accuracy, _config, ruined);

            if (DecoratesInPan)
            {
                FinishToppings(ruined, accuracy);
            }

            // Kick off the pan's flip gesture and the whimsical serve together.
            Coroutine panTilt = _pan != null ? StartCoroutine(_pan.Flip(null, accuracy)) : null;

            if (_waffleStack != null)
            {
                yield return _panPancake != null
                    ? _waffleStack.LaunchWaffle(_panPancake, accuracy, _config, ruined)
                    : _waffleStack.ProduceWaffle(accuracy, _config, ruined);
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

            _cookedPancakes.Add(new PancakeRecord(accuracy, ruined));
            session?.RecordWaffle(accuracy, roundIndex);

            if (session != null)
            {
                _hud?.SetWaffleCount(session.TotalWaffles);
                _hud?.SetAccuracy(session.CumulativeAccuracy);
            }
        }

        // A pancake still in the pan when time runs out was never finished.
        ThrowAwayPanPancake();

        _sequenceRunner?.StopRound();
        _roundActive = false;
        _state = GameState.RoundEnd;
    }

    // Worst a pancake can score while still being under the mistake budget. It
    // sits clear of WaffleController's cooked threshold, which is what keeps a
    // one- or two-mistake pancake golden.
    private const float ShakyBakeQuality = 0.7f;

    /// <summary>
    /// Turns the pancake's mistake count into the 0..1 bake quality the pan,
    /// stack and serving round already speak. Quality slides down across the
    /// mistake budget and only falls off the cliff once the budget is spent, so
    /// nothing short of the third mistake ruins a pancake.
    /// </summary>
    private float BakeQuality(int wrongPresses)
    {
        int budget = _config.MistakesBeforeRuined;
        if (wrongPresses >= budget)
        {
            return 0f;
        }

        return Mathf.Lerp(1f, ShakyBakeQuality, wrongPresses / (float)budget);
    }

    /// <summary>True once a first-to-N match has been won.</summary>
    private bool MatchDecided => _sequenceRunner?.Match?.IsDecided ?? false;

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

    /// <summary>
    /// Finishes the pancake in the pan. Only a ruined one gets its toppings
    /// thrown about; a shaky-but-saved pancake simply goes without the cherry.
    /// </summary>
    private void FinishToppings(bool ruined, float accuracy)
    {
        if (_panToppings == null)
        {
            return;
        }

        if (ruined)
        {
            _decorator.Jumble(_panToppings, Mathf.Lerp(0.35f, 1f, 1f - accuracy));
            return;
        }

        // A clean run through the whole sequence earns the cherry.
        if (_wrongPresses == 0)
        {
            _decorator.AddCherry(_panToppings, _panFrame);
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

    /// <summary>Each resolved token drops a topping onto the pancake in the pan.</summary>
    private void HandleTokenCompleted(int teamIndex, int slot, int playerIndex)
    {
        if (teamIndex != CookingTeamIndex || !DecoratesInPan || _panToppings == null)
        {
            return;
        }

        int tokenCount = _sequenceRunner?.Team(CookingTeamIndex)?.Tokens.Count ?? 0;
        if (tokenCount > 0 && slot < tokenCount)
        {
            _decorator.AddPiece(_panToppings, _panFrame, slot, tokenCount, true);
        }
    }

    /// <summary>
    /// A wrong press shakes the screen, and the mistake that spends the team's
    /// budget scorches the pancake on the spot. The ones before it are free.
    /// </summary>
    private void HandleWrongPress(int teamIndex, int offendingPlayer)
    {
        if (teamIndex != CookingTeamIndex)
        {
            return;
        }

        _wrongPresses++;
        _cameraShake?.Shake();

        if (!DecoratesInPan || _panPancake == null || _wrongPresses < _config.MistakesBeforeRuined)
        {
            return;
        }

        WaffleController panWaffle = _panPancake.GetComponent<WaffleController>();
        if (panWaffle != null)
        {
            panWaffle.SetCharLevel(0f, _config, true);
        }
    }

    private void HandleSequenceComplete(int teamIndex, int wrongPresses)
    {
        if (teamIndex == CookingTeamIndex)
        {
            // The running count is kept rather than the state's own tally: a
            // pancake can take several attempts, and every mistake across all of
            // them counts against it. The state's tally covers one attempt.
            _outcome = SequenceOutcome.Completed;
        }
    }

    private void HandleSequenceTimeout(int teamIndex)
    {
        if (teamIndex == CookingTeamIndex)
        {
            _outcome = SequenceOutcome.TimedOut;
        }
    }
}
