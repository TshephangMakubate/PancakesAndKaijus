using NUnit.Framework;

/// <summary>
/// Coverage for the 2v2 wrapper: seed fairness, per-team independence,
/// press routing and the win conditions.
/// </summary>
public class SequenceMatchTests
{
    private const int TeamA = 0;
    private const int TeamB = 1;
    private const float Frame = 1f / 60f;

    private static SequenceSettings Settings(bool sharedSeed = true)
    {
        SequenceSettings settings = SequenceSettings.Default;
        settings.ModifierResolveDelay = 0f;
        settings.SequenceSeconds = 100f;
        settings.UseSharedSeed = sharedSeed;
        return settings;
    }

    [Test]
    public void SharedSeed_DealsBothTeamsTheSameSequence()
    {
        var match = new SequenceMatch(Settings(sharedSeed: true), seed: 2024);

        match.DealTo(TeamA);
        match.DealTo(TeamB);

        CollectionAssert.AreEqual(match.Team(TeamA).Tokens, match.Team(TeamB).Tokens);
    }

    [Test]
    public void SharedSeed_KeepsTeamsInStepEvenWhenOneRunsAhead()
    {
        var match = new SequenceMatch(Settings(sharedSeed: true), seed: 7);

        // Team A burns through three sequences before team B sees its first.
        match.DealTo(TeamA);
        match.DealTo(TeamA);
        match.DealTo(TeamA);
        var thirdForA = new System.Collections.Generic.List<SequenceToken>(match.Team(TeamA).Tokens);

        match.DealTo(TeamB);
        match.DealTo(TeamB);
        match.DealTo(TeamB);

        CollectionAssert.AreEqual(thirdForA, match.Team(TeamB).Tokens,
            "The Nth sequence must be identical for both teams regardless of pace.");
    }

    [Test]
    public void IndependentSeed_CanDealDifferentSequences()
    {
        var match = new SequenceMatch(Settings(sharedSeed: false), seed: 11);

        match.DealTo(TeamA);
        match.DealTo(TeamB);

        // Not guaranteed different on any single deal, so sweep a few.
        bool differed = false;
        for (int i = 0; i < 10 && !differed; i++)
        {
            for (int t = 0; t < match.Team(TeamA).Tokens.Count; t++)
            {
                if (!match.Team(TeamA).Tokens[t].Equals(match.Team(TeamB).Tokens[t]))
                {
                    differed = true;
                    break;
                }
            }

            match.DealTo(TeamA);
            match.DealTo(TeamB);
        }

        Assert.IsTrue(differed, "Independent seeds should diverge.");
    }

    [Test]
    public void Press_RoutesByGlobalPlayerIndex()
    {
        var match = new SequenceMatch(Settings(), seed: 3);
        match.DealTo(TeamA);
        match.DealTo(TeamB);

        int expectedSeat = match.Team(TeamA).ExpectedPlayerAtCursor();

        // Global indices 0 and 1 are team A's seats; 2 and 3 are team B's.
        match.Press(expectedSeat);
        match.Tick(Frame);

        // The cursor may land past slot 1 when a zero-delay modifier follows.
        Assert.Greater(match.Team(TeamA).Cursor, 0, "Team A should have advanced.");
        Assert.AreEqual(0, match.Team(TeamB).Cursor, "Team B must be untouched by team A's input.");
    }

    [Test]
    public void InactiveTeam_IsNeverDealtOrTicked()
    {
        var match = new SequenceMatch(Settings(), seed: 5);
        match.SetTeamActive(TeamB, false);

        match.DealTo(TeamA);
        match.DealTo(TeamB);

        Assert.IsTrue(match.Team(TeamA).IsActive);
        Assert.AreEqual(0, match.Team(TeamB).SequenceNumber, "An inactive team should never be dealt a sequence.");
    }

    [Test]
    public void MostInRoundTimer_IsNeverDecidedEarly()
    {
        SequenceSettings settings = Settings();
        settings.WinCondition = WinConditionMode.MostInRoundTimer;

        var match = new SequenceMatch(settings, seed: 1);
        CompleteSequences(match, TeamA, 3);

        Assert.IsFalse(match.IsDecided, "A round-timer match only ends when the caller's clock does.");
        Assert.AreEqual(TeamA, match.LeadingTeam);
    }

    [Test]
    public void FirstToCount_IsDecidedOnReachingTheTarget()
    {
        SequenceSettings settings = Settings();
        settings.WinCondition = WinConditionMode.FirstToCount;
        settings.SequencesToWin = 2;

        var match = new SequenceMatch(settings, seed: 1);
        Assert.IsFalse(match.IsDecided);

        CompleteSequences(match, TeamA, 2);

        Assert.IsTrue(match.IsDecided);
        Assert.AreEqual(TeamA, match.LeadingTeam);
    }

    [Test]
    public void DealTo_AppliesTheDifficultyRampPerSequence()
    {
        SequenceSettings settings = Settings();
        settings.StartTokenCount = 3;
        settings.MaxTokenCount = 8;
        settings.SequencesPerLengthStep = 1;

        var match = new SequenceMatch(settings, seed: 12);
        SequenceState state = match.Team(TeamA);

        match.DealTo(TeamA);
        Assert.AreEqual(3, state.Tokens.Count, "The first sequence should be the short one.");

        match.DealTo(TeamA);
        Assert.AreEqual(4, state.Tokens.Count, "The row should grow by one each sequence.");

        for (int i = 0; i < 10; i++)
        {
            match.DealTo(TeamA);
        }

        Assert.AreEqual(8, state.Tokens.Count, "The ramp should hold at the maximum length.");
    }

    [Test]
    public void DealTo_GivesLongerSequencesMoreTime()
    {
        SequenceSettings settings = Settings();
        settings.BaseSeconds = 2f;
        settings.SecondsPerToken = 0.9f;

        var match = new SequenceMatch(settings, seed: 12);
        SequenceState state = match.Team(TeamA);

        match.DealTo(TeamA);
        float shortSequence = state.TimeRemaining;

        for (int i = 0; i < 6; i++)
        {
            match.DealTo(TeamA);
        }

        Assert.Greater(state.TimeRemaining, shortSequence, "A longer row should come with a longer clock.");
    }

    [Test]
    public void SharedSeed_RampsBothTeamsIdentically()
    {
        var match = new SequenceMatch(Settings(sharedSeed: true), seed: 31);

        // Team A races ahead; team B's third row must still match team A's third.
        for (int i = 0; i < 3; i++)
        {
            match.DealTo(TeamA);
        }

        var thirdForA = new System.Collections.Generic.List<SequenceToken>(match.Team(TeamA).Tokens);

        for (int i = 0; i < 3; i++)
        {
            match.DealTo(TeamB);
        }

        CollectionAssert.AreEqual(thirdForA, match.Team(TeamB).Tokens);
    }

    [Test]
    public void LeadingTeam_IsMinusOneWhenLevel()
    {
        var match = new SequenceMatch(Settings(), seed: 1);
        Assert.AreEqual(-1, match.LeadingTeam);
    }

    /// <summary>Drives a team through whole sequences by always pressing correctly.</summary>
    private static void CompleteSequences(SequenceMatch match, int team, int count)
    {
        SequenceState state = match.Team(team);
        for (int i = 0; i < count; i++)
        {
            match.DealTo(team);

            int guard = 0;
            while (state.IsActive && guard++ < 500)
            {
                int seat = state.ExpectedPlayerAtCursor();
                if (seat >= 0)
                {
                    state.Press(seat);
                }

                match.Tick(Frame);
            }
        }
    }
}
