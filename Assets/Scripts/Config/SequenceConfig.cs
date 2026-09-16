using UnityEngine;

/// <summary>
/// Inspector front-end for the co-op sequence mechanic. Everything the rules
/// need is authored here and handed to the pure logic as a plain
/// <see cref="SequenceSettings"/>, so no MonoBehaviour or asset type ever
/// reaches <c>SequenceState</c>.
/// </summary>
[CreateAssetMenu(fileName = "SequenceConfig", menuName = "Waffle Party/Sequence Config")]
public class SequenceConfig : ScriptableObject
{
    private const int DefaultStartTokenCount = 3;
    private const int DefaultMaxTokenCount = 8;
    private const int DefaultSequencesPerLengthStep = 1;
    private const int DefaultSequencesBeforeModifiers = 2;
    private const float DefaultBaseSeconds = 2f;
    private const float DefaultSecondsPerToken = 0.9f;
    private const float DefaultModifierChance = 0.28f;
    private const int DefaultMinModifiers = 1;
    private const int DefaultMaxModifiers = 3;
    private const float DefaultModifierResolveDelay = 0.35f;
    private const float DefaultWrongPressSeconds = 1f;
    private const int DefaultSequencesToWin = 5;
    private const string DefaultPlayer1Key = "<Keyboard>/f";
    private const string DefaultPlayer2Key = "<Keyboard>/j";
    private const string DefaultPlayer3Key = "<Keyboard>/v";
    private const string DefaultPlayer4Key = "<Keyboard>/n";
    private const string DefaultGamepadButton = "<Gamepad>/buttonSouth";

    [Header("Difficulty Ramp")]
    [Tooltip("Length of the first sequence of a round.")]
    [SerializeField, Min(2)] private int _startTokenCount = DefaultStartTokenCount;
    [Tooltip("Length the ramp tops out at.")]
    [SerializeField, Min(2)] private int _maxTokenCount = DefaultMaxTokenCount;
    [Tooltip("Sequences completed before the row grows by one token.")]
    [SerializeField, Min(1)] private int _sequencesPerLengthStep = DefaultSequencesPerLengthStep;
    [Tooltip("Opening sequences kept free of swap and shuffle tokens, so players learn the colour mapping first.")]
    [SerializeField, Min(0)] private int _sequencesBeforeModifiers = DefaultSequencesBeforeModifiers;

    [Header("Sequence Shape")]
    [Tooltip("Chance that an eligible interior slot becomes a modifier token.")]
    [SerializeField, Range(0f, 1f)] private float _modifierChance = DefaultModifierChance;
    [SerializeField, Min(0)] private int _minModifiers = DefaultMinModifiers;
    [SerializeField, Min(0)] private int _maxModifiers = DefaultMaxModifiers;
    [Tooltip("Re-roll a shuffle until it actually changes the order, where that is possible.")]
    [SerializeField] private bool _guaranteeShuffleChangesOrder = true;

    [Header("Timing")]
    [Tooltip("Flat time every sequence gets, before the per-token allowance.")]
    [SerializeField, Min(0f)] private float _baseSeconds = DefaultBaseSeconds;
    [Tooltip("Extra seconds per token, so a longer row gets longer to solve. Set to 0 and raise Base Seconds for a flat timer.")]
    [SerializeField, Min(0f)] private float _secondsPerToken = DefaultSecondsPerToken;
    [Tooltip("Pause after a modifier resolves before the cursor advances. Input is ignored during it.")]
    [SerializeField, Min(0f)] private float _modifierResolveDelay = DefaultModifierResolveDelay;

    [Header("Penalties")]
    [SerializeField] private WrongPressPenaltyMode _wrongPressPenalty = WrongPressPenaltyMode.TimeDeduction;
    [Tooltip("Seconds lost per wrong press when the penalty is a time deduction.")]
    [SerializeField, Min(0f)] private float _wrongPressSeconds = DefaultWrongPressSeconds;
    [SerializeField] private TimeoutConsequence _timeout = TimeoutConsequence.NewSequenceOnly;

    [Header("Match")]
    [SerializeField] private WinConditionMode _winCondition = WinConditionMode.MostInRoundTimer;
    [Tooltip("Target used when the win condition is first-to-count.")]
    [SerializeField, Min(1)] private int _sequencesToWin = DefaultSequencesToWin;
    [Tooltip("Deal both teams the identical sequence so neither gets an easier row.")]
    [SerializeField] private bool _useSharedSeed = true;
    [Tooltip("Leave at 0 to pick a fresh seed each run; set non-zero for a repeatable match.")]
    [SerializeField] private int _seed;

    [Header("Teams")]
    [Tooltip("Team A (the left station) plays. Normally on.")]
    [SerializeField] private bool _teamAActive = true;
    [Tooltip("Team B (the right station) plays. Off until you have four players.")]
    [SerializeField] private bool _teamBActive;

    [Header("Accessibility")]
    [Tooltip("Show a small HUD hint while the mapping is flipped. Off by default - remembering the flip is the mechanic.")]
    [SerializeField] private bool _showSwappedIndicator;

    [Header("Input Bindings")]
    [Tooltip("Team A, player 1 (owns the orange channel before any swap).")]
    [SerializeField] private string _player1Key = DefaultPlayer1Key;
    [Tooltip("Team A, player 2 (owns the blue channel before any swap).")]
    [SerializeField] private string _player2Key = DefaultPlayer2Key;
    [Tooltip("Team B, player 1.")]
    [SerializeField] private string _player3Key = DefaultPlayer3Key;
    [Tooltip("Team B, player 2.")]
    [SerializeField] private string _player4Key = DefaultPlayer4Key;
    [Tooltip("Gamepad button used by every player; each player is paired to a different pad by join order.")]
    [SerializeField] private string _gamepadButton = DefaultGamepadButton;
    [Tooltip("Pair players to gamepads in the order the pads are detected.")]
    [SerializeField] private bool _useGamepads = true;

    /// <summary>Whether a team takes part this match.</summary>
    public bool IsTeamActive(int teamIndex)
    {
        return teamIndex == 0 ? _teamAActive : _teamBActive;
    }

    /// <summary>Authored seed; 0 means pick a fresh one per run.</summary>
    public int Seed => _seed;

    /// <summary>Small HUD hint while the mapping is flipped.</summary>
    public bool ShowSwappedIndicator => _showSwappedIndicator;

    /// <summary>True when players should also be paired to gamepads.</summary>
    public bool UseGamepads => _useGamepads;

    /// <summary>Input System path of the shared gamepad button.</summary>
    public string GamepadButton => _gamepadButton;

    /// <summary>Keyboard binding path for a flat 0..3 player index.</summary>
    public string KeyFor(int globalPlayerIndex)
    {
        switch (globalPlayerIndex)
        {
            case 0: return _player1Key;
            case 1: return _player2Key;
            case 2: return _player3Key;
            case 3: return _player4Key;
            default: return string.Empty;
        }
    }

    /// <summary>Packs the authored values into the plain settings the rules consume.</summary>
    public SequenceSettings ToSettings()
    {
        var settings = new SequenceSettings
        {
            TokenCount = _startTokenCount,
            StartTokenCount = _startTokenCount,
            MaxTokenCount = _maxTokenCount,
            SequencesPerLengthStep = _sequencesPerLengthStep,
            SequencesBeforeModifiers = _sequencesBeforeModifiers,
            BaseSeconds = _baseSeconds,
            SecondsPerToken = _secondsPerToken,
            ModifierChance = _modifierChance,
            MinModifiers = _minModifiers,
            MaxModifiers = _maxModifiers,
            ModifierResolveDelay = _modifierResolveDelay,
            SequenceSeconds = _baseSeconds + (_secondsPerToken * _startTokenCount),
            WrongPressPenalty = _wrongPressPenalty,
            WrongPressSeconds = _wrongPressSeconds,
            Timeout = _timeout,
            GuaranteeShuffleChangesOrder = _guaranteeShuffleChangesOrder,
            UseSharedSeed = _useSharedSeed,
            ShowSwappedIndicator = _showSwappedIndicator,
            WinCondition = _winCondition,
            SequencesToWin = _sequencesToWin
        };

        return settings.Validated();
    }

    /// <summary>The seed to run with, honouring 0 as "pick one".</summary>
    public int ResolveSeed()
    {
        return _seed != 0 ? _seed : Random.Range(int.MinValue, int.MaxValue);
    }
}
