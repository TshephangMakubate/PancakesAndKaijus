using UnityEngine;

/// <summary>
/// Tunable game settings for the Waffle Party cooking prototype.
/// Kept as a ScriptableObject so round count, timing, sequence length,
/// scoring weights, and char colors can be tuned without code edits.
/// </summary>
[CreateAssetMenu(fileName = "GameConfig", menuName = "Waffle Party/Game Config")]
public class GameConfig : ScriptableObject
{
    private const int DefaultRoundCount = 2;
    private const float DefaultRoundDuration = 30f;
    private const float DefaultStepDisplaySeconds = 0.5f;
    private const float DefaultInputWindowSeconds = 0f;
    private const float DefaultWrongPressPenalty = 0.5f;
    private const float DefaultExtraPressPenalty = 0.5f;
    private const int DefaultCookingRoundCount = 2;
    private const string DefaultDecoratingSceneName = "Decorating Scene";
    private const string DefaultServingSceneName = "Serving Scene";
    private const float DefaultServingRoundSeconds = 45f;

    [Header("Rounds")]
    [SerializeField] private int _roundCount = DefaultRoundCount;
    [SerializeField] private float _roundDurationSeconds = DefaultRoundDuration;
    [SerializeField] private int[] _sequenceLengthPerRound = { 3, 4, 4 };

    [Header("Decorating")]
    [Tooltip("Rounds played in the cooking scene; the remaining rounds are played in the decorating scene.")]
    [SerializeField] private int _cookingRoundCount = DefaultCookingRoundCount;
    [Tooltip("Scene loaded after the cooking rounds. Must be listed in Build Settings.")]
    [SerializeField] private string _decoratingSceneName = DefaultDecoratingSceneName;

    [Header("Serving")]
    [Tooltip("Final round's scene, where plates are slid to customers. Leave empty to end the match after cooking.")]
    [SerializeField] private string _servingSceneName = DefaultServingSceneName;
    [SerializeField] private float _servingRoundSeconds = DefaultServingRoundSeconds;

    [Header("Timing")]
    [SerializeField] private float _stepDisplaySeconds = DefaultStepDisplaySeconds;
    [Tooltip("Per-sequence input timeout in seconds. 0 disables the timeout.")]
    [SerializeField] private float _inputWindowSeconds = DefaultInputWindowSeconds;

    [Header("Scoring Penalties")]
    [SerializeField] private float _wrongPressPenalty = DefaultWrongPressPenalty;
    [SerializeField] private float _extraPressPenalty = DefaultExtraPressPenalty;

    [Header("Waffle Char Colors")]
    [SerializeField] private Color _waffleRawColor = new Color(0.93f, 0.85f, 0.62f);
    [SerializeField] private Color _waffleGoldenColor = new Color(0.80f, 0.55f, 0.20f);
    [SerializeField] private Color _waffleCharredColor = new Color(0.15f, 0.10f, 0.07f);

    /// <summary>Number of cooking rounds in a full match.</summary>
    public int RoundCount => _roundCount;

    /// <summary>Duration of each round in seconds.</summary>
    public float RoundDurationSeconds => _roundDurationSeconds;

    /// <summary>Target sequence length per round (clamped to last entry).</summary>
    public int[] SequenceLengthPerRound => _sequenceLengthPerRound;

    /// <summary>Rounds played cooking before switching to decorating.</summary>
    public int CookingRoundCount => Mathf.Clamp(_cookingRoundCount, 1, Mathf.Max(1, _roundCount));

    /// <summary>Scene that hosts the decorating rounds.</summary>
    public string DecoratingSceneName => _decoratingSceneName;

    /// <summary>True when some rounds are left over for decorating after cooking.</summary>
    public bool HasDecoratingRounds => CookingRoundCount < _roundCount && !string.IsNullOrEmpty(_decoratingSceneName);

    /// <summary>Scene that hosts the final serving round.</summary>
    public string ServingSceneName => _servingSceneName;

    /// <summary>Length of the serving round in seconds.</summary>
    public float ServingRoundSeconds => _servingRoundSeconds;

    /// <summary>True when a serving round follows the cooking rounds.</summary>
    public bool HasServingRound => !string.IsNullOrEmpty(_servingSceneName);

    /// <summary>Round total shown on the HUD, counting the serving round.</summary>
    public int DisplayedRoundCount => _roundCount + (HasServingRound ? 1 : 0);

    /// <summary>Per-arrow reveal time during sequence playback.</summary>
    public float StepDisplaySeconds => _stepDisplaySeconds;

    /// <summary>Optional per-sequence input timeout; 0 means no timeout.</summary>
    public float InputWindowSeconds => _inputWindowSeconds;

    /// <summary>Accuracy penalty applied per mismatched slot.</summary>
    public float WrongPressPenalty => _wrongPressPenalty;

    /// <summary>Accuracy penalty applied per surplus press beyond target length.</summary>
    public float ExtraPressPenalty => _extraPressPenalty;

    /// <summary>Color for a raw (unbaked) waffle.</summary>
    public Color WaffleRawColor => _waffleRawColor;

    /// <summary>Color for a perfectly golden waffle.</summary>
    public Color WaffleGoldenColor => _waffleGoldenColor;

    /// <summary>Color for a fully charred waffle.</summary>
    public Color WaffleCharredColor => _waffleCharredColor;

    /// <summary>
    /// Returns the target sequence length for a round, clamped to valid range.
    /// </summary>
    public int GetSequenceLength(int roundIndex)
    {
        if (_sequenceLengthPerRound == null || _sequenceLengthPerRound.Length == 0)
        {
            return 3;
        }

        int clamped = Mathf.Clamp(roundIndex, 0, _sequenceLengthPerRound.Length - 1);
        return Mathf.Max(1, _sequenceLengthPerRound[clamped]);
    }
}
