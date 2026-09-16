using System;
using NUnit.Framework;

/// <summary>
/// Constraint coverage for <see cref="TokenSequenceGenerator"/>. The structural
/// rules are asserted across many seeds, because a generator that only usually
/// holds its invariants will produce an unplayable row eventually.
/// </summary>
public class TokenSequenceGeneratorTests
{
    private const int SeedSweep = 200;

    private static SequenceSettings Settings(Action<SequenceSettings> tweak = null)
    {
        SequenceSettings settings = SequenceSettings.Default;
        tweak?.Invoke(settings);
        return settings;
    }

    private static SequenceToken[] Generate(SequenceSettings settings, int seed)
    {
        return new TokenSequenceGenerator().Generate(settings, new Random(seed));
    }

    [Test]
    public void Generate_ProducesTheRequestedLength()
    {
        SequenceSettings settings = Settings(s => { });
        settings.TokenCount = 8;

        for (int seed = 0; seed < SeedSweep; seed++)
        {
            Assert.AreEqual(8, Generate(settings, seed).Length);
        }
    }

    [Test]
    public void Generate_FirstTileNeverCarriesAnAction()
    {
        SequenceSettings settings = Settings();

        for (int seed = 0; seed < SeedSweep; seed++)
        {
            SequenceToken[] tokens = Generate(settings, seed);
            Assert.IsFalse(tokens[0].HasAction, $"seed {seed}");
        }
    }

    [Test]
    public void Generate_LastTileNeverCarriesAnAction()
    {
        SequenceSettings settings = Settings();

        for (int seed = 0; seed < SeedSweep; seed++)
        {
            SequenceToken[] tokens = Generate(settings, seed);
            Assert.IsFalse(tokens[tokens.Length - 1].HasAction, $"seed {seed}");
        }
    }

    [Test]
    public void Generate_NeverPlacesTwoActionsAdjacently()
    {
        // Push the modifier rate hard so adjacency would show up if unguarded.
        SequenceSettings settings = Settings();
        settings.ModifierChance = 1f;
        settings.MaxModifiers = 4;

        for (int seed = 0; seed < SeedSweep; seed++)
        {
            SequenceToken[] tokens = Generate(settings, seed);
            for (int i = 1; i < tokens.Length; i++)
            {
                bool adjacent = tokens[i].HasAction && tokens[i - 1].HasAction;
                Assert.IsFalse(adjacent, $"seed {seed}: actions adjacent at {i - 1} and {i}");
            }
        }
    }

    [Test]
    public void Generate_EveryShuffleHasAtLeastTwoTilesAfterIt()
    {
        SequenceSettings settings = Settings();
        settings.ModifierChance = 1f;
        settings.MaxModifiers = 4;

        for (int seed = 0; seed < SeedSweep; seed++)
        {
            SequenceToken[] tokens = Generate(settings, seed);
            for (int i = 0; i < tokens.Length; i++)
            {
                if (tokens[i].Action != TokenAction.Shuffle)
                {
                    continue;
                }

                int tilesAfter = tokens.Length - i - 1;
                Assert.GreaterOrEqual(tilesAfter, 2, $"seed {seed}: shuffle at {i} has nothing to rearrange");
            }
        }
    }

    [Test]
    public void Generate_RespectsTheActionCountRange()
    {
        SequenceSettings settings = Settings();
        settings.TokenCount = 10;
        settings.ModifierChance = 0.5f;
        settings.MinModifiers = 1;
        settings.MaxModifiers = 2;

        for (int seed = 0; seed < SeedSweep; seed++)
        {
            SequenceToken[] tokens = Generate(settings, seed);
            int actions = 0;
            for (int i = 0; i < tokens.Length; i++)
            {
                if (tokens[i].HasAction)
                {
                    actions++;
                }
            }

            Assert.GreaterOrEqual(actions, 1, $"seed {seed}");
            Assert.LessOrEqual(actions, 2, $"seed {seed}");
        }
    }

    [Test]
    public void Generate_WithZeroMaxModifiers_ProducesPlainTilesOnly()
    {
        SequenceSettings settings = Settings();
        settings.MinModifiers = 0;
        settings.MaxModifiers = 0;

        SequenceToken[] tokens = Generate(settings, 1);
        foreach (SequenceToken token in tokens)
        {
            Assert.IsFalse(token.HasAction);
        }
    }

    [Test]
    public void Generate_WithTheSameSeed_IsIdentical()
    {
        SequenceSettings settings = Settings();

        SequenceToken[] first = Generate(settings, 4242);
        SequenceToken[] second = Generate(settings, 4242);

        CollectionAssert.AreEqual(first, second, "Shared-seed fairness depends on this.");
    }

    [Test]
    public void Generate_WithDifferentSeeds_EventuallyDiffers()
    {
        SequenceSettings settings = Settings();
        SequenceToken[] baseline = Generate(settings, 1);

        bool sawDifference = false;
        for (int seed = 2; seed < 30 && !sawDifference; seed++)
        {
            SequenceToken[] other = Generate(settings, seed);
            for (int i = 0; i < baseline.Length && !sawDifference; i++)
            {
                if (!baseline[i].Equals(other[i]))
                {
                    sawDifference = true;
                }
            }
        }

        Assert.IsTrue(sawDifference, "Different seeds should produce different sequences.");
    }

    [Test]
    public void Generate_ClampsAnUnplayableTokenCount()
    {
        SequenceSettings settings = Settings();
        settings.TokenCount = -5;

        SequenceToken[] tokens = Generate(settings, 1);

        Assert.AreEqual(2, tokens.Length, "A degenerate length should clamp, not throw.");
        Assert.IsFalse(tokens[0].HasAction);
    }

    // --- Difficulty ramp -------------------------------------------------

    [Test]
    public void ForSequence_GrowsFromStartLengthUpToMaxThenHolds()
    {
        SequenceSettings settings = Settings();
        settings.StartTokenCount = 3;
        settings.MaxTokenCount = 8;
        settings.SequencesPerLengthStep = 1;

        Assert.AreEqual(3, settings.ForSequence(0).TokenCount);
        Assert.AreEqual(4, settings.ForSequence(1).TokenCount);
        Assert.AreEqual(8, settings.ForSequence(5).TokenCount);

        // The ramp tops out rather than running away.
        Assert.AreEqual(8, settings.ForSequence(6).TokenCount);
        Assert.AreEqual(8, settings.ForSequence(50).TokenCount);
    }

    [Test]
    public void ForSequence_HonoursASlowerLengthStep()
    {
        SequenceSettings settings = Settings();
        settings.StartTokenCount = 3;
        settings.MaxTokenCount = 8;
        settings.SequencesPerLengthStep = 3;

        Assert.AreEqual(3, settings.ForSequence(0).TokenCount);
        Assert.AreEqual(3, settings.ForSequence(2).TokenCount);
        Assert.AreEqual(4, settings.ForSequence(3).TokenCount);
    }

    [Test]
    public void ForSequence_KeepsModifiersOutOfTheOpeningSequences()
    {
        SequenceSettings settings = Settings();
        settings.SequencesBeforeModifiers = 2;
        settings.ModifierChance = 1f;

        // The opening rows must be pure button tokens whatever the seed.
        for (int seed = 0; seed < 40; seed++)
        {
            foreach (int index in new[] { 0, 1 })
            {
                SequenceToken[] tokens = Generate(settings.ForSequence(index), seed);
                foreach (SequenceToken token in tokens)
                {
                    Assert.IsFalse(token.HasAction, $"seed {seed}, sequence {index}");
                }
            }
        }
    }

    [Test]
    public void ForSequence_AllowsModifiersOnceTheOpeningSequencesArePast()
    {
        SequenceSettings settings = Settings();
        settings.SequencesBeforeModifiers = 2;
        settings.ModifierChance = 1f;
        settings.MinModifiers = 1;

        bool sawModifier = false;
        for (int seed = 0; seed < 40 && !sawModifier; seed++)
        {
            foreach (SequenceToken token in Generate(settings.ForSequence(4), seed))
            {
                if (token.HasAction)
                {
                    sawModifier = true;
                    break;
                }
            }
        }

        Assert.IsTrue(sawModifier, "Modifiers should appear once the ramp is past its opening sequences.");
    }

    [Test]
    public void ForSequence_ScalesTheClockWithTheRowLength()
    {
        SequenceSettings settings = Settings();
        settings.StartTokenCount = 3;
        settings.MaxTokenCount = 8;
        settings.BaseSeconds = 2f;
        settings.SecondsPerToken = 0.9f;

        Assert.AreEqual(2f + (0.9f * 3f), settings.ForSequence(0).SequenceSeconds, 0.001f);
        Assert.AreEqual(2f + (0.9f * 8f), settings.ForSequence(5).SequenceSeconds, 0.001f);
    }

    [Test]
    public void ForSequence_SurvivesAnInvertedLengthRange()
    {
        SequenceSettings settings = Settings();
        settings.StartTokenCount = 9;
        settings.MaxTokenCount = 4;

        // Max below Start should clamp rather than produce a negative range.
        int length = settings.ForSequence(0).TokenCount;
        Assert.GreaterOrEqual(length, 2);
        Assert.AreEqual(length, settings.ForSequence(10).TokenCount);
    }

    [Test]
    public void Generate_NullRandom_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new TokenSequenceGenerator().Generate(Settings(), null));
    }
}
