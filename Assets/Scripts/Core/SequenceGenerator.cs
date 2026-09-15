using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Produces randomized directional sequences of a requested length.
/// Supports an optional seed so party fairness can be made deterministic.
/// </summary>
[Serializable]
public class SequenceGenerator
{
    private static readonly Direction[] AllDirections =
    {
        Direction.Up,
        Direction.Down,
        Direction.Left,
        Direction.Right
    };

    private System.Random _random;

    /// <summary>Creates a generator seeded from the current time.</summary>
    public SequenceGenerator()
    {
        _random = new System.Random();
    }

    /// <summary>Creates a generator with a deterministic seed.</summary>
    public SequenceGenerator(int seed)
    {
        _random = new System.Random(seed);
    }

    /// <summary>Reseeds the generator for deterministic playback.</summary>
    public void Reseed(int seed)
    {
        _random = new System.Random(seed);
    }

    /// <summary>
    /// Generates a randomized directional sequence of the requested length.
    /// </summary>
    public List<Direction> Generate(int length)
    {
        int count = Mathf.Max(0, length);
        var sequence = new List<Direction>(count);
        for (int i = 0; i < count; i++)
        {
            int index = _random.Next(AllDirections.Length);
            sequence.Add(AllDirections[index]);
        }

        return sequence;
    }
}
