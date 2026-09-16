using System;
using UnityEngine;

/// <summary>
/// The scene-side host for the co-op sequence mechanic. It owns the pure
/// <see cref="SequenceMatch"/>, wires the four player buttons to it, pumps the
/// clock each frame, and re-broadcasts each team's events with the team index
/// attached.
/// <para>
/// With <c>AutoDeal</c> on it runs standalone, which is what the test scene
/// uses. With it off, a caller such as <see cref="GameManager"/> decides when
/// the next sequence is dealt so it can interleave its own animations.
/// </para>
/// </summary>
public class SequenceMatchRunner : MonoBehaviour
{
    [Header("Config")]
    [SerializeField] private SequenceConfig _config;

    [Header("Stations")]
    [Tooltip("View for team A (the left station).")]
    [SerializeField] private SequenceStationView _teamAStation;
    [Tooltip("View for team B (the right station).")]
    [SerializeField] private SequenceStationView _teamBStation;

    [Header("Behaviour")]
    [Tooltip("Deal a fresh sequence automatically when one finishes. Leave off when a game manager drives the pacing.")]
    [SerializeField] private bool _autoDeal = true;
    [Tooltip("Pause before the next sequence is dealt in auto mode.")]
    [SerializeField, Min(0f)] private float _autoDealDelay = 0.6f;
    [Tooltip("Start the round as soon as the scene plays. Leave off when a game manager announces rounds first.")]
    [SerializeField] private bool _startOnPlay;

    private SequenceMatch _match;
    private SequenceInputBinder _input;
    private readonly float[] _autoDealTimer = new float[SequenceMatch.TeamCount];
    private bool _running;

    /// <summary>Raised when a team finishes a sequence. Carries the team and its wrong-press count.</summary>
    public event Action<int, int> OnTeamSequenceComplete;

    /// <summary>Raised when a team's clock runs out. Carries the team.</summary>
    public event Action<int> OnTeamTimeout;

    /// <summary>Raised when a team resolves a token. Carries team, slot and player.</summary>
    public event Action<int, int, int> OnTeamTokenCompleted;

    /// <summary>Raised when a team presses wrong. Carries team and the offending seat.</summary>
    public event Action<int, int> OnTeamWrongPress;

    /// <summary>The live match, for scores and win-condition queries.</summary>
    public SequenceMatch Match => _match;

    /// <summary>True while the clock is being pumped.</summary>
    public bool IsRunning => _running;

    private void Awake()
    {
        BuildMatch();
    }

    private void Start()
    {
        if (_startOnPlay)
        {
            StartRound();
        }
    }

    private void OnDestroy()
    {
        TearDownEvents();
        _input?.Dispose();
        _input = null;
    }

    private void Update()
    {
        if (!_running || _match == null)
        {
            return;
        }

        _match.Tick(Time.deltaTime);

        if (_autoDeal)
        {
            TickAutoDeal(Time.deltaTime);
        }
    }

    /// <summary>Begins pumping the clock and enables input.</summary>
    public void StartRound()
    {
        if (_match == null)
        {
            BuildMatch();
        }

        _running = true;
        _input?.Enable();

        if (!_autoDeal)
        {
            return;
        }

        for (int team = 0; team < SequenceMatch.TeamCount; team++)
        {
            if (_match.IsTeamActive(team))
            {
                _match.DealTo(team);
            }
        }
    }

    /// <summary>Stops the clock and disables input.</summary>
    public void StopRound()
    {
        _running = false;
        _input?.Disable();

        for (int team = 0; team < SequenceMatch.TeamCount; team++)
        {
            _match?.Team(team).Stop();
        }
    }

    /// <summary>Deals a fresh sequence to a team.</summary>
    public void DealTo(int teamIndex)
    {
        _match?.DealTo(teamIndex);
    }

    /// <summary>Puts the team's current sequence back on the board from the top.</summary>
    public void RedealTo(int teamIndex)
    {
        _match?.RedealTo(teamIndex);
    }

    /// <summary>
    /// Drives every station's bar from an outside clock — the round timer the
    /// game manager owns — rather than from each sequence's own countdown. Pass a
    /// non-positive duration to hand the bars back to the sequence clock.
    /// </summary>
    public void SetRoundTimer(float secondsRemaining, float durationSeconds)
    {
        _teamAStation?.SetRoundTimer(secondsRemaining, durationSeconds);
        _teamBStation?.SetRoundTimer(secondsRemaining, durationSeconds);
    }

    /// <summary>Rows stop timing out and wrong presses stop resetting them; the round clock is the only pressure.</summary>
    public void UseRoundClockOnly()
    {
        if (_match == null)
        {
            BuildMatch();
        }

        _match?.UseRoundClockOnly();
    }

    /// <summary>The state for a team, for callers that want to read the cursor directly.</summary>
    public SequenceState Team(int teamIndex)
    {
        return _match?.Team(teamIndex);
    }

    private void BuildMatch()
    {
        if (_config == null)
        {
            Debug.LogError("SequenceMatchRunner: SequenceConfig is not assigned.");
            return;
        }

        _match = new SequenceMatch(_config.ToSettings(), _config.ResolveSeed());

        for (int team = 0; team < SequenceMatch.TeamCount; team++)
        {
            _match.SetTeamActive(team, _config.IsTeamActive(team));
        }

        BindStation(_teamAStation, 0);
        BindStation(_teamBStation, 1);

        _input = new SequenceInputBinder(_config);
        _input.OnPlayerPressed += HandlePlayerPressed;

        HookTeamEvents(0);
        HookTeamEvents(1);
    }

    private void BindStation(SequenceStationView station, int teamIndex)
    {
        if (station == null)
        {
            return;
        }

        bool active = _match.IsTeamActive(teamIndex);
        station.gameObject.SetActive(active);

        if (active)
        {
            station.Bind(_match.Team(teamIndex), _config.ShowSwappedIndicator);
        }
    }

    private void HookTeamEvents(int teamIndex)
    {
        SequenceState state = _match.Team(teamIndex);
        int team = teamIndex;

        state.OnSequenceComplete += wrong =>
        {
            ScheduleAutoDeal(team);
            OnTeamSequenceComplete?.Invoke(team, wrong);
        };

        state.OnTimeout += () =>
        {
            ScheduleAutoDeal(team);
            OnTeamTimeout?.Invoke(team);
        };

        state.OnTokenCompleted += (slot, player) => OnTeamTokenCompleted?.Invoke(team, slot, player);
        state.OnWrongPress += (offender, expected) => OnTeamWrongPress?.Invoke(team, offender);
    }

    private void TearDownEvents()
    {
        if (_input != null)
        {
            _input.OnPlayerPressed -= HandlePlayerPressed;
        }

        _teamAStation?.Unbind();
        _teamBStation?.Unbind();
    }

    private void HandlePlayerPressed(int globalPlayerIndex)
    {
        if (_running)
        {
            _match?.Press(globalPlayerIndex);
        }
    }

    private void ScheduleAutoDeal(int teamIndex)
    {
        if (_autoDeal)
        {
            _autoDealTimer[teamIndex] = Mathf.Max(0.01f, _autoDealDelay);
        }
    }

    private void TickAutoDeal(float deltaTime)
    {
        for (int team = 0; team < _autoDealTimer.Length; team++)
        {
            if (_autoDealTimer[team] <= 0f)
            {
                continue;
            }

            _autoDealTimer[team] -= deltaTime;
            if (_autoDealTimer[team] <= 0f)
            {
                _autoDealTimer[team] = 0f;
                _match.DealTo(team);
            }
        }
    }
}
