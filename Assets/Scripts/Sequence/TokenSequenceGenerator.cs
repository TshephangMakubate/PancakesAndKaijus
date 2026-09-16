using System;
using System.Collections.Generic;

/// <summary>
/// Builds token sequences that always satisfy the mechanic's constraints.
/// <para>
/// Every tile belongs to one of the two colour channels. A minority of them
/// also carry an action, which the tile's owner fires by pressing it. Actions
/// never sit on the first or last tile, never sit next to each other, and a
/// shuffle always has at least two tiles after it to rearrange.
/// </para>
/// <para>
/// Generation is driven by a caller-supplied <see cref="Random"/>, so two teams
/// handed the same seed receive byte-identical sequences.
/// </para>
/// </summary>
public class TokenSequenceGenerator
{
    private static readonly TokenChannel[] AllChannels = { TokenChannel.Orange, TokenChannel.Blue };
    private static readonly TokenAction[] AllActions = { TokenAction.Swap, TokenAction.Shuffle };

    // A shuffle is only worth placing when this many tiles follow it.
    private const int MinTilesAfterShuffle = 2;

    private readonly List<int> _candidates = new List<int>();
    private readonly List<int> _chosen = new List<int>();

    /// <summary>
    /// Generates one sequence. The same <paramref name="settings"/> and an
    /// equally-seeded <paramref name="random"/> always produce the same result.
    /// </summary>
    public SequenceToken[] Generate(SequenceSettings settings, Random random)
    {
        if (random == null)
        {
            throw new ArgumentNullException(nameof(random));
        }

        SequenceSettings safe = settings.Validated();
        var tokens = new SequenceToken[safe.TokenCount];

        // Colour every tile first; actions are then layered onto some of them.
        for (int i = 0; i < tokens.Length; i++)
        {
            tokens[i] = SequenceToken.Plain(AllChannels[random.Next(AllChannels.Length)]);
        }

        PickActionSlots(safe, random);
        PlaceActions(tokens, random);

        if (safe.GuaranteeShuffleChangesOrder)
        {
            EnsureShufflesCanMatter(tokens, random);
        }

        return tokens;
    }

    /// <summary>
    /// Chooses which interior tiles carry an action, honouring the min/max count
    /// and the "never adjacent" rule. Leaving the first and last tile plain
    /// means a sequence always opens and closes on a simple press.
    /// </summary>
    private void PickActionSlots(SequenceSettings settings, Random random)
    {
        _candidates.Clear();
        _chosen.Clear();

        for (int i = 1; i < settings.TokenCount - 1; i++)
        {
            _candidates.Add(i);
        }

        if (_candidates.Count == 0 || settings.MaxModifiers == 0)
        {
            return;
        }

        // Sample how many actions this sequence wants, then clamp to range.
        int wanted = 0;
        for (int i = 0; i < _candidates.Count; i++)
        {
            if (random.NextDouble() < settings.ModifierChance)
            {
                wanted++;
            }
        }

        wanted = Math.Max(settings.MinModifiers, Math.Min(settings.MaxModifiers, wanted));
        if (wanted == 0)
        {
            return;
        }

        Shuffle(_candidates, random);

        // Greedily accept slots that don't touch one we've already taken, so
        // players always get a plain tile between two actions.
        for (int i = 0; i < _candidates.Count && _chosen.Count < wanted; i++)
        {
            int slot = _candidates[i];
            if (!IsAdjacentToChosen(slot))
            {
                _chosen.Add(slot);
            }
        }

        _chosen.Sort();
    }

    private bool IsAdjacentToChosen(int slot)
    {
        for (int i = 0; i < _chosen.Count; i++)
        {
            if (Math.Abs(_chosen[i] - slot) <= 1)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Layers an action onto each chosen tile, keeping the tile's colour. A
    /// shuffle without enough tiles after it is demoted to a swap.
    /// </summary>
    private void PlaceActions(SequenceToken[] tokens, Random random)
    {
        for (int i = 0; i < _chosen.Count; i++)
        {
            int slot = _chosen[i];
            TokenAction action = AllActions[random.Next(AllActions.Length)];

            if (action == TokenAction.Shuffle && tokens.Length - slot - 1 < MinTilesAfterShuffle)
            {
                action = TokenAction.Swap;
            }

            tokens[slot] = SequenceToken.WithAction(tokens[slot].Channel, action);
        }
    }

    /// <summary>
    /// A shuffle whose trailing tiles are all identical would visibly do
    /// nothing. Where that happens, recolour one of them so the rearrangement
    /// is always observable.
    /// </summary>
    private void EnsureShufflesCanMatter(SequenceToken[] tokens, Random random)
    {
        for (int i = 0; i < _chosen.Count; i++)
        {
            int slot = _chosen[i];
            if (tokens[slot].Action != TokenAction.Shuffle)
            {
                continue;
            }

            int first = slot + 1;
            if (first >= tokens.Length)
            {
                continue;
            }

            bool mixed = false;
            for (int j = first + 1; j < tokens.Length && !mixed; j++)
            {
                if (!tokens[j].Equals(tokens[first]))
                {
                    mixed = true;
                }
            }

            if (mixed)
            {
                continue;
            }

            // All the same: flip a random trailing tile to the other channel.
            int target = first + 1 + random.Next(Math.Max(1, tokens.Length - first - 1));
            if (target >= tokens.Length)
            {
                continue;
            }

            TokenChannel flipped = tokens[target].Channel == TokenChannel.Orange
                ? TokenChannel.Blue
                : TokenChannel.Orange;
            tokens[target] = tokens[target].WithChannel(flipped);
        }
    }

    /// <summary>In-place Fisher-Yates using the supplied deterministic source.</summary>
    private static void Shuffle(List<int> values, Random random)
    {
        for (int i = values.Count - 1; i > 0; i--)
        {
            int j = random.Next(i + 1);
            (values[i], values[j]) = (values[j], values[i]);
        }
    }
}
