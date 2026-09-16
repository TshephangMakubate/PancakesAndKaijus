using System;

/// <summary>What a wrong press costs the team.</summary>
public enum WrongPressPenaltyMode
{
    /// <summary>Deduct time from the sequence clock and carry on where we were.</summary>
    TimeDeduction,

    /// <summary>Send the cursor back to the start, clearing the swap and restoring the original order.</summary>
    ResetCursor,

    /// <summary>Nothing but the mistake count: the cursor stays on the token until the right player presses it.</summary>
    StayOnToken
}

/// <summary>What happens when the sequence clock runs out.</summary>
public enum TimeoutConsequence
{
    /// <summary>The sequence is simply abandoned and a fresh one is dealt.</summary>
    NewSequenceOnly,

    /// <summary>As above, but the team also loses a point if it has any.</summary>
    NewSequenceAndScorePenalty
}

/// <summary>How a match between the two teams is decided.</summary>
public enum WinConditionMode
{
    /// <summary>Whoever completed the most sequences when the round clock expires.</summary>
    MostInRoundTimer,

    /// <summary>First team to reach <c>SequencesToWin</c> ends the round immediately.</summary>
    FirstToCount
}

/// <summary>
/// Every tuning value the sequence rules need, as plain data with no Unity
/// dependency, so Edit Mode tests can build a configuration inline without a
/// ScriptableObject. <c>SequenceConfig</c> is the authored inspector front-end
/// that produces one of these.
/// <para>
/// Public mutable fields are deliberate here: this is a parameter bag that
/// tests tweak field-by-field, not an inspector-serialized type.
/// </para>
/// </summary>
[Serializable]
public struct SequenceSettings
{
    /// <summary>
    /// Token slots in the sequence being played. Derived per sequence by
    /// <see cref="ForSequence"/> from the ramp below; set it directly only in
    /// tests that want a fixed length.
    /// </summary>
    public int TokenCount;

    /// <summary>Length of the very first sequence of a round.</summary>
    public int StartTokenCount;

    /// <summary>Length the ramp tops out at.</summary>
    public int MaxTokenCount;

    /// <summary>Sequences completed before the row grows by one token.</summary>
    public int SequencesPerLengthStep;

    /// <summary>Opening sequences that stay pure button tokens, easing players in.</summary>
    public int SequencesBeforeModifiers;

    /// <summary>Chance (0..1) that an eligible slot becomes a modifier.</summary>
    public float ModifierChance;

    /// <summary>Fewest modifiers a generated sequence may contain.</summary>
    public int MinModifiers;

    /// <summary>Most modifiers a generated sequence may contain.</summary>
    public int MaxModifiers;

    /// <summary>Pause after a modifier resolves before the cursor advances. Input is ignored during it.</summary>
    public float ModifierResolveDelay;

    /// <summary>
    /// Seconds on the clock for the sequence being played. Derived per sequence
    /// by <see cref="ForSequence"/> from the two values below.
    /// </summary>
    public float SequenceSeconds;

    /// <summary>Flat time every sequence gets, before the per-token allowance.</summary>
    public float BaseSeconds;

    /// <summary>Extra seconds granted per token, so a longer row gets longer to solve.</summary>
    public float SecondsPerToken;

    /// <summary>What a wrong press costs.</summary>
    public WrongPressPenaltyMode WrongPressPenalty;

    /// <summary>Seconds removed by a wrong press when the penalty is <see cref="WrongPressPenaltyMode.TimeDeduction"/>.</summary>
    public float WrongPressSeconds;

    /// <summary>What running out of time costs.</summary>
    public TimeoutConsequence Timeout;

    /// <summary>
    /// When true a sequence has no clock of its own and never times out; an
    /// outside round timer is the only pressure. Off by default so the
    /// standalone test scene keeps its per-row countdown.
    /// </summary>
    public bool NoSequenceClock;

    /// <summary>When true, a shuffle re-draws until the order actually changes (where that is possible).</summary>
    public bool GuaranteeShuffleChangesOrder;

    /// <summary>When true, both teams are dealt the identical sequence from a shared seed.</summary>
    public bool UseSharedSeed;

    /// <summary>Optional HUD hint that the mapping is currently flipped. Off by default — remembering is the mechanic.</summary>
    public bool ShowSwappedIndicator;

    /// <summary>How the match is decided.</summary>
    public WinConditionMode WinCondition;

    /// <summary>Target used by <see cref="WinConditionMode.FirstToCount"/>.</summary>
    public int SequencesToWin;

    /// <summary>A sensible starting point that satisfies every generation constraint.</summary>
    public static SequenceSettings Default => new SequenceSettings
    {
        TokenCount = 3,
        StartTokenCount = 3,
        MaxTokenCount = 8,
        SequencesPerLengthStep = 1,
        SequencesBeforeModifiers = 2,
        BaseSeconds = 2f,
        SecondsPerToken = 0.9f,
        ModifierChance = 0.28f,
        MinModifiers = 1,
        MaxModifiers = 3,
        ModifierResolveDelay = 0.35f,
        SequenceSeconds = 8f,
        WrongPressPenalty = WrongPressPenaltyMode.TimeDeduction,
        WrongPressSeconds = 1f,
        Timeout = TimeoutConsequence.NewSequenceOnly,
        GuaranteeShuffleChangesOrder = true,
        UseSharedSeed = true,
        ShowSwappedIndicator = false,
        WinCondition = WinConditionMode.MostInRoundTimer,
        SequencesToWin = 5
    };

    /// <summary>
    /// The settings for one sequence of a round, applying the difficulty ramp:
    /// the row grows from <see cref="StartTokenCount"/> towards
    /// <see cref="MaxTokenCount"/>, modifiers stay out of the opening
    /// sequences, and the clock scales with the row's length.
    /// </summary>
    /// <param name="sequenceIndex">Zero-based index of the sequence in the round.</param>
    public SequenceSettings ForSequence(int sequenceIndex)
    {
        SequenceSettings result = this;
        int index = Math.Max(0, sequenceIndex);

        int step = Math.Max(1, SequencesPerLengthStep);
        int floor = Math.Max(2, Math.Min(StartTokenCount, MaxTokenCount));
        int ceiling = Math.Max(floor, MaxTokenCount);
        result.TokenCount = Clamp(floor + (index / step), floor, ceiling);

        // The first sequences are pure button tokens so players learn the
        // colour-to-key mapping before it starts being messed with.
        if (index < SequencesBeforeModifiers)
        {
            result.MinModifiers = 0;
            result.MaxModifiers = 0;
        }

        result.SequenceSeconds = BaseSeconds + (SecondsPerToken * result.TokenCount);

        return result.Validated();
    }

    /// <summary>
    /// Clamps authored values into a range the generator and state machine can
    /// always satisfy, so a bad inspector entry degrades instead of throwing.
    /// </summary>
    public SequenceSettings Validated()
    {
        SequenceSettings result = this;

        // Two slots is the floor: a sequence must open and close on a button token.
        result.TokenCount = Math.Max(2, result.TokenCount);
        result.StartTokenCount = Math.Max(2, result.StartTokenCount);
        result.MaxTokenCount = Math.Max(result.StartTokenCount, result.MaxTokenCount);
        result.SequencesPerLengthStep = Math.Max(1, result.SequencesPerLengthStep);
        result.SequencesBeforeModifiers = Math.Max(0, result.SequencesBeforeModifiers);
        result.BaseSeconds = Math.Max(0f, result.BaseSeconds);
        result.SecondsPerToken = Math.Max(0f, result.SecondsPerToken);
        result.ModifierChance = Clamp01(result.ModifierChance);
        result.ModifierResolveDelay = Math.Max(0f, result.ModifierResolveDelay);
        result.SequenceSeconds = Math.Max(0.1f, result.SequenceSeconds);
        result.WrongPressSeconds = Math.Max(0f, result.WrongPressSeconds);
        result.SequencesToWin = Math.Max(1, result.SequencesToWin);

        // Slots 0 and TokenCount-1 are always buttons, and modifiers may not be
        // adjacent, so at most every other interior slot can hold one.
        int modifierCeiling = Math.Max(0, (result.TokenCount - 1) / 2);
        result.MaxModifiers = Clamp(result.MaxModifiers, 0, modifierCeiling);
        result.MinModifiers = Clamp(result.MinModifiers, 0, result.MaxModifiers);

        return result;
    }

    private static float Clamp01(float value)
    {
        return value < 0f ? 0f : value > 1f ? 1f : value;
    }

    private static int Clamp(int value, int min, int max)
    {
        return value < min ? min : value > max ? max : value;
    }
}
