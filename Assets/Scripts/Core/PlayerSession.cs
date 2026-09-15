using System.Collections.Generic;

/// <summary>
/// Holds all per-player state so the game generalizes to N players.
/// <see cref="GameManager"/> iterates a list of sessions and never stores
/// score directly, so adding players is simply adding sessions to the list.
/// </summary>
public class PlayerSession
{
    /// <summary>Directional input source for this player.</summary>
    public DirectionalInputReader Input { get; }

    /// <summary>Stable identifier for this player.</summary>
    public int PlayerId { get; }

    /// <summary>Average copy accuracy recorded per round.</summary>
    public List<float> PerRoundAccuracy { get; } = new List<float>();

    /// <summary>Number of waffles produced per round.</summary>
    public List<int> PerRoundWaffleCount { get; } = new List<int>();

    /// <summary>Number of pancakes decorated and served.</summary>
    public int DecoratedCount { get; private set; }

    private float _accuracyTotal;
    private int _accuracySamples;
    private float _decorationTotal;

    public PlayerSession(int playerId, DirectionalInputReader input)
    {
        PlayerId = playerId;
        Input = input;
    }

    /// <summary>Cumulative average accuracy across all waffles produced.</summary>
    public float CumulativeAccuracy => _accuracySamples > 0 ? _accuracyTotal / _accuracySamples : 0f;

    /// <summary>Average accuracy across all decorated pancakes.</summary>
    public float DecorationAccuracy => DecoratedCount > 0 ? _decorationTotal / DecoratedCount : 0f;

    /// <summary>Total number of waffles produced across all rounds.</summary>
    public int TotalWaffles
    {
        get
        {
            int total = 0;
            for (int i = 0; i < PerRoundWaffleCount.Count; i++)
            {
                total += PerRoundWaffleCount[i];
            }

            return total;
        }
    }

    /// <summary>Prepares per-round tracking lists for a new round.</summary>
    public void BeginRound(int roundIndex)
    {
        while (PerRoundAccuracy.Count <= roundIndex)
        {
            PerRoundAccuracy.Add(0f);
        }

        while (PerRoundWaffleCount.Count <= roundIndex)
        {
            PerRoundWaffleCount.Add(0);
        }
    }

    /// <summary>
    /// Records a completed waffle, updating round and cumulative tallies.
    /// </summary>
    public void RecordWaffle(float accuracy, int roundIndex)
    {
        BeginRound(roundIndex);

        int count = PerRoundWaffleCount[roundIndex];
        float roundAverage = PerRoundAccuracy[roundIndex];
        float newRoundTotal = (roundAverage * count) + accuracy;

        count++;
        PerRoundWaffleCount[roundIndex] = count;
        PerRoundAccuracy[roundIndex] = newRoundTotal / count;

        _accuracyTotal += accuracy;
        _accuracySamples++;
    }

    /// <summary>Records a decorated pancake.</summary>
    public void RecordDecoration(float accuracy)
    {
        _decorationTotal += accuracy;
        DecoratedCount++;
    }

    /// <summary>Continues another session's stats, e.g. carrying cooking results into decorating.</summary>
    public void CopyStatsFrom(PlayerSession other)
    {
        if (other == null)
        {
            return;
        }

        PerRoundAccuracy.Clear();
        PerRoundAccuracy.AddRange(other.PerRoundAccuracy);
        PerRoundWaffleCount.Clear();
        PerRoundWaffleCount.AddRange(other.PerRoundWaffleCount);
        _accuracyTotal = other._accuracyTotal;
        _accuracySamples = other._accuracySamples;
        _decorationTotal = other._decorationTotal;
        DecoratedCount = other.DecoratedCount;
    }
}
