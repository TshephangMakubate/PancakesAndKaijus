using System.Collections.Generic;
using UnityEngine;

/// <summary>How a single cooked pancake turned out, so it can be rebuilt in another scene.</summary>
public readonly struct PancakeRecord
{
    public readonly float Accuracy;
    public readonly bool Burned;

    public PancakeRecord(float accuracy, bool burned)
    {
        Accuracy = accuracy;
        Burned = burned;
    }
}

/// <summary>Carries the cooked pancakes and player stats from the cooking scene into the decorating scene.</summary>
public static class PancakeCarryover
{
    private static readonly List<PancakeRecord> _pancakes = new List<PancakeRecord>();

    /// <summary>Pancakes cooked in the order they were stacked (last = top of the pile).</summary>
    public static IReadOnlyList<PancakeRecord> Pancakes => _pancakes;

    /// <summary>The cooking session whose stats continue into decorating.</summary>
    public static PlayerSession Session { get; private set; }

    /// <summary>True once a cooking scene has handed off its results.</summary>
    public static bool HasData => Session != null;

    public static void Store(PlayerSession session, IEnumerable<PancakeRecord> pancakes)
    {
        Session = session;
        _pancakes.Clear();
        _pancakes.AddRange(pancakes);
    }

    public static void Clear()
    {
        Session = null;
        _pancakes.Clear();
    }

    // Statics survive play sessions when domain reload is disabled.
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    private static void ResetOnPlay()
    {
        Clear();
    }
}
