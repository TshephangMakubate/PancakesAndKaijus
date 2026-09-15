using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Detailed accuracy breakdown for a single copied sequence.
/// </summary>
public struct AccuracyResult
{
    /// <summary>Normalized accuracy in the range [0, 1].</summary>
    public float Accuracy;

    /// <summary>Number of presses that matched the target in order.</summary>
    public int CorrectCount;

    /// <summary>Number of in-range presses that did not match.</summary>
    public int WrongCount;

    /// <summary>Number of presses beyond the target length.</summary>
    public int ExtraCount;
}

/// <summary>
/// Compares a player's entered inputs against the target sequence and
/// returns a normalized accuracy used for scoring and waffle char level.
/// </summary>
[Serializable]
public class AccuracyScorer
{
    /// <summary>
    /// Scores the entered sequence against the target, returning [0, 1].
    /// </summary>
    public float Score(IReadOnlyList<Direction> target, IReadOnlyList<Direction> entered, GameConfig config)
    {
        return Evaluate(target, entered, config).Accuracy;
    }

    /// <summary>
    /// Produces a full accuracy breakdown against the target sequence.
    /// </summary>
    public AccuracyResult Evaluate(IReadOnlyList<Direction> target, IReadOnlyList<Direction> entered, GameConfig config)
    {
        var result = new AccuracyResult();

        int targetCount = target?.Count ?? 0;
        if (targetCount == 0)
        {
            result.Accuracy = 0f;
            return result;
        }

        int enteredCount = entered?.Count ?? 0;

        int correct = 0;
        int wrong = 0;
        int compared = Mathf.Min(targetCount, enteredCount);
        for (int i = 0; i < compared; i++)
        {
            if (entered[i] == target[i])
            {
                correct++;
            }
            else
            {
                wrong++;
            }
        }

        // Missing presses are treated as wrong slots.
        int missing = targetCount - enteredCount;
        if (missing > 0)
        {
            wrong += missing;
        }

        int extra = Mathf.Max(0, enteredCount - targetCount);

        float wrongPenalty = config != null ? config.WrongPressPenalty : 0.5f;
        float extraPenalty = config != null ? config.ExtraPressPenalty : 0.5f;

        float rawScore = correct - (wrong * wrongPenalty) - (extra * extraPenalty);
        float accuracy = Mathf.Clamp01(rawScore / targetCount);

        result.Accuracy = accuracy;
        result.CorrectCount = correct;
        result.WrongCount = wrong;
        result.ExtraCount = extra;
        return result;
    }
}
