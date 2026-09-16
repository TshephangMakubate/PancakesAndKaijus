using System;
using System.Collections.Generic;
using NUnit.Framework;

/// <summary>
/// Rules coverage for <see cref="SequenceState"/>. Every test drives the state
/// machine through <c>Press</c>/<c>Tick</c> only, exactly as the view layer does.
/// <para>
/// Every tile belongs to a colour channel; some also carry an action that fires
/// when that tile's owner presses it.
/// </para>
/// </summary>
public class SequenceStateTests
{
    private const int Player1 = 0;
    private const int Player2 = 1;
    private const float Frame = 1f / 60f;

    private static SequenceToken Orange => SequenceToken.Plain(TokenChannel.Orange);
    private static SequenceToken Blue => SequenceToken.Plain(TokenChannel.Blue);

    private static SequenceToken OrangeSwap => SequenceToken.WithAction(TokenChannel.Orange, TokenAction.Swap);
    private static SequenceToken BlueSwap => SequenceToken.WithAction(TokenChannel.Blue, TokenAction.Swap);
    private static SequenceToken OrangeShuffle => SequenceToken.WithAction(TokenChannel.Orange, TokenAction.Shuffle);

    /// <summary>Settings with instant actions, so tests don't have to burn the delay.</summary>
    private static SequenceSettings Instant()
    {
        SequenceSettings settings = SequenceSettings.Default;
        settings.ModifierResolveDelay = 0f;
        settings.SequenceSeconds = 100f;
        return settings;
    }

    private static SequenceState NewState(SequenceSettings settings, int seed = 1234)
    {
        return new SequenceState(settings, new Random(seed));
    }

    /// <summary>Presses and then ends the frame, which is how the runner drives it.</summary>
    private static void PressAndTick(SequenceState state, int player)
    {
        state.Press(player);
        state.Tick(Frame);
    }

    // --- Ownership ------------------------------------------------------

    [Test]
    public void OwnerOf_MapsOrangeToPlayer1_AndBlueToPlayer2()
    {
        Assert.AreEqual(Player1, SequenceState.OwnerOf(TokenChannel.Orange));
        Assert.AreEqual(Player2, SequenceState.OwnerOf(TokenChannel.Blue));
    }

    [Test]
    public void ExpectedPlayer_IsOwnerXorSwapped()
    {
        SequenceState state = NewState(Instant());
        state.StartSequence(new[] { Orange, OrangeSwap, Orange });

        // Before the swap, orange belongs to player 1.
        Assert.AreEqual(Player1, state.ExpectedPlayerAtCursor());
        PressAndTick(state, Player1);

        // The swap tile is itself orange, so player 1 owns it too.
        Assert.AreEqual(Player1, state.ExpectedPlayerAtCursor());
        PressAndTick(state, Player1);

        // Having fired, orange now belongs to player 2.
        Assert.IsTrue(state.IsSwapped);
        Assert.AreEqual(Player2, state.ExpectedPlayerAtCursor());
    }

    // --- Presses --------------------------------------------------------

    [Test]
    public void CorrectPress_CompletesTileAndAdvancesCursor()
    {
        SequenceState state = NewState(Instant());
        var completed = new List<int>();
        state.OnTokenCompleted += (slot, player) => completed.Add(slot);
        state.StartSequence(new[] { Orange, Blue });

        state.Press(Player1);

        Assert.AreEqual(new[] { 0 }, completed.ToArray());
        Assert.AreEqual(1, state.Cursor);
        Assert.AreEqual(0, state.WrongPressCount);
    }

    [Test]
    public void WrongPress_DeductsTimeAndLeavesCursorInPlace()
    {
        SequenceSettings settings = Instant();
        settings.WrongPressPenalty = WrongPressPenaltyMode.TimeDeduction;
        settings.WrongPressSeconds = 2f;

        SequenceState state = NewState(settings);
        state.StartSequence(new[] { Orange, Blue });
        float before = state.TimeRemaining;

        // Player 2 presses on an orange tile, which player 1 owns.
        PressAndTick(state, Player2);

        Assert.AreEqual(1, state.WrongPressCount);
        Assert.AreEqual(0, state.Cursor, "A wrong press must not advance the cursor.");
        Assert.Less(state.TimeRemaining, before - 2f + 0.001f);
    }

    [Test]
    public void WrongPress_RaisesEventWithOffenderAndExpectedPlayer()
    {
        SequenceState state = NewState(Instant());
        int offender = -1;
        int expected = -1;
        state.OnWrongPress += (o, e) => { offender = o; expected = e; };
        state.StartSequence(new[] { Orange });

        PressAndTick(state, Player2);

        Assert.AreEqual(Player2, offender);
        Assert.AreEqual(Player1, expected);
    }

    [Test]
    public void WrongPress_ResetCursorMode_RestoresCursorSwapAndOriginalOrder()
    {
        SequenceSettings settings = Instant();
        settings.WrongPressPenalty = WrongPressPenaltyMode.ResetCursor;

        SequenceState state = NewState(settings);
        SequenceToken[] original = { Orange, OrangeSwap, Blue, OrangeShuffle, Blue, Orange, Blue };
        state.StartSequence(original);

        PressAndTick(state, Player1); // slot 0, orange
        PressAndTick(state, Player1); // slot 1, orange swap tile
        Assert.IsTrue(state.IsSwapped);

        PressAndTick(state, Player1); // slot 2, blue under a swap belongs to player 1
        PressAndTick(state, Player2); // slot 3, orange shuffle tile under a swap
        Assert.AreEqual(4, state.Cursor, "Expected the shuffle tile to have been pressed.");

        bool resetRaised = false;
        state.OnSequenceReset += () => resetRaised = true;

        // Deliberately press with the wrong player.
        PressAndTick(state, 1 - state.ExpectedPlayerAtCursor());

        Assert.IsTrue(resetRaised);
        Assert.AreEqual(0, state.Cursor);
        Assert.IsFalse(state.IsSwapped, "A reset must clear the swap state.");
        CollectionAssert.AreEqual(original, state.Tokens, "A reset must restore the original order.");
    }

    [Test]
    public void Press_AfterSequenceComplete_IsIgnored()
    {
        SequenceState state = NewState(Instant());
        state.StartSequence(new[] { Orange });

        state.Press(Player1);
        Assert.IsFalse(state.IsActive);

        PressAndTick(state, Player2);

        Assert.AreEqual(0, state.WrongPressCount);
    }

    // --- Action tiles ----------------------------------------------------

    [Test]
    public void ActionTile_FiresOnlyWhenItsOwnerPresses()
    {
        SequenceState state = NewState(Instant());

        // The swap tile is blue, so player 2 owns it.
        state.StartSequence(new[] { Orange, BlueSwap, Orange });
        PressAndTick(state, Player1);

        Assert.AreEqual(Player2, state.ExpectedPlayerAtCursor());

        // Player 1 grabbing it is simply a wrong press.
        PressAndTick(state, Player1);
        Assert.AreEqual(1, state.WrongPressCount);
        Assert.IsFalse(state.IsSwapped, "A wrong press must not fire the tile's action.");
        Assert.AreEqual(1, state.Cursor, "A wrong press must not advance past an action tile.");

        // The owner presses and the action fires.
        PressAndTick(state, Player2);
        Assert.IsTrue(state.IsSwapped);
        Assert.AreEqual(2, state.Cursor);
    }

    [Test]
    public void PlainTile_FiresNoAction()
    {
        SequenceState state = NewState(Instant());
        bool swapped = false;
        state.OnSwap += _ => swapped = true;
        state.StartSequence(new[] { Orange, Blue });

        PressAndTick(state, Player1);

        Assert.IsFalse(swapped);
    }

    [Test]
    public void ActionDelay_IgnoresInputThenAdvances()
    {
        SequenceSettings settings = Instant();
        settings.ModifierResolveDelay = 0.35f;

        SequenceState state = NewState(settings);
        state.StartSequence(new[] { Orange, OrangeSwap, Blue });

        PressAndTick(state, Player1); // slot 0
        state.Press(Player1);         // slot 1, the swap tile
        state.Tick(0.1f);

        Assert.IsTrue(state.IsResolvingAction);
        Assert.AreEqual(1, state.Cursor, "The cursor holds on the tile while its action plays out.");
        Assert.IsTrue(state.IsSwapped, "The effect lands immediately; only the advance waits.");

        // Presses during the delay do nothing at all.
        state.Press(Player1);
        state.Press(Player2);
        state.Tick(0.1f);
        Assert.AreEqual(0, state.WrongPressCount, "Presses during an action delay must be ignored.");

        state.Tick(0.4f);
        Assert.AreEqual(2, state.Cursor);
        Assert.IsFalse(state.IsResolvingAction);
    }

    // --- Swap -----------------------------------------------------------

    [Test]
    public void Swap_FlipsOwnershipForTheRestOfTheSequence()
    {
        SequenceState state = NewState(Instant());
        bool? reported = null;
        state.OnSwap += value => reported = value;
        state.StartSequence(new[] { Orange, OrangeSwap, Orange });

        PressAndTick(state, Player1);
        PressAndTick(state, Player1);

        Assert.IsTrue(reported);
        Assert.IsTrue(state.IsSwapped);

        // Orange is now player 2's responsibility.
        state.Press(Player2);
        Assert.AreEqual(0, state.WrongPressCount);
        Assert.IsFalse(state.IsActive, "Sequence should have completed.");
    }

    [Test]
    public void DoubleSwap_RestoresTheOriginalOwnership()
    {
        SequenceState state = NewState(Instant());
        state.StartSequence(new[] { OrangeSwap, Blue, BlueSwap, Orange });

        PressAndTick(state, Player1); // slot 0, orange swap -> swapped on
        Assert.IsTrue(state.IsSwapped);

        PressAndTick(state, Player1); // slot 1, blue under swap -> player 1
        PressAndTick(state, Player1); // slot 2, blue swap under swap -> player 1
        Assert.IsFalse(state.IsSwapped, "A second swap must flip the mapping back.");

        state.Press(Player1);         // slot 3, orange back to player 1
        Assert.AreEqual(0, state.WrongPressCount);
        Assert.IsFalse(state.IsActive);
    }

    // --- Shuffle --------------------------------------------------------

    [Test]
    public void Shuffle_MovesOnlyTheTilesStillToCome()
    {
        SequenceState state = NewState(Instant());
        SequenceToken[] tokens = { Orange, Blue, OrangeShuffle, Orange, Blue, Blue, Orange };
        state.StartSequence(tokens);

        PressAndTick(state, Player1); // slot 0 orange
        PressAndTick(state, Player2); // slot 1 blue
        PressAndTick(state, Player1); // slot 2, the orange shuffle tile

        IReadOnlyList<SequenceToken> after = state.Tokens;

        Assert.AreEqual(tokens[0], after[0], "A resolved tile must never move.");
        Assert.AreEqual(tokens[1], after[1], "A resolved tile must never move.");
        Assert.AreEqual(tokens[2], after[2], "The shuffle tile itself must stay put.");

        // Slots 3..6 are permuted among themselves, so the multiset holds.
        var before = new List<SequenceToken> { tokens[3], tokens[4], tokens[5], tokens[6] };
        var now = new List<SequenceToken> { after[3], after[4], after[5], after[6] };
        CollectionAssert.AreEquivalent(before, now);
    }

    [Test]
    public void Shuffle_WithAFixedSeed_IsDeterministic()
    {
        SequenceToken[] tokens = { OrangeShuffle, Blue, Orange, Blue, Orange, Blue };

        IReadOnlyList<SequenceToken> first = RunShuffleOnce(tokens, seed: 99);
        IReadOnlyList<SequenceToken> second = RunShuffleOnce(tokens, seed: 99);
        CollectionAssert.AreEqual(first, second, "The same seed must produce the same permutation.");
    }

    [Test]
    public void Shuffle_GuaranteeChangesOrder_ActuallyReordersMixedTiles()
    {
        SequenceSettings settings = Instant();
        settings.GuaranteeShuffleChangesOrder = true;

        SequenceToken[] tokens = { OrangeShuffle, Orange, Blue, Orange, Blue, Blue };

        SequenceState state = NewState(settings, seed: 7);
        state.StartSequence(tokens);
        PressAndTick(state, Player1);

        var before = new List<SequenceToken> { tokens[1], tokens[2], tokens[3], tokens[4], tokens[5] };
        var now = new List<SequenceToken>
        {
            state.Tokens[1], state.Tokens[2], state.Tokens[3], state.Tokens[4], state.Tokens[5]
        };

        CollectionAssert.AreNotEqual(before, now, "The shuffle should visibly change the order.");
        CollectionAssert.AreEquivalent(before, now);
    }

    [Test]
    public void Shuffle_ReportsTokenIdsBeforeAndAfter()
    {
        SequenceState state = NewState(Instant());
        int[] oldOrder = null;
        int[] newOrder = null;
        state.OnShuffle += (o, n) => { oldOrder = o; newOrder = n; };

        state.StartSequence(new[] { OrangeShuffle, Blue, Orange, Blue, Orange });
        PressAndTick(state, Player1);

        Assert.IsNotNull(oldOrder);
        Assert.IsNotNull(newOrder);
        Assert.AreEqual(5, oldOrder.Length);
        CollectionAssert.AreEquivalent(oldOrder, newOrder, "A shuffle permutes ids, it never invents or drops them.");
        Assert.AreEqual(oldOrder[0], newOrder[0], "The shuffle tile keeps its slot.");
    }

    private static IReadOnlyList<SequenceToken> RunShuffleOnce(SequenceToken[] tokens, int seed)
    {
        SequenceState state = NewState(Instant(), seed);
        state.StartSequence(tokens);
        PressAndTick(state, Player1);
        return new List<SequenceToken>(state.Tokens);
    }

    // --- Simultaneous presses -------------------------------------------

    [Test]
    public void SimultaneousPress_CorrectFirst_IgnoresTheOtherWithoutPenalty()
    {
        SequenceState state = NewState(Instant());
        state.StartSequence(new[] { Orange, Blue });

        state.Press(Player1); // correct
        state.Press(Player2); // same frame, must be ignored
        state.Tick(Frame);

        Assert.AreEqual(0, state.WrongPressCount, "The losing simultaneous press must not be penalized.");
        Assert.AreEqual(1, state.Cursor);
    }

    [Test]
    public void SimultaneousPress_WrongFirst_StillSucceedsWithoutPenalty()
    {
        SequenceState state = NewState(Instant());
        state.StartSequence(new[] { Orange, Blue });

        state.Press(Player2); // wrong, held pending
        state.Press(Player1); // correct, same frame, cancels the mistake
        state.Tick(Frame);

        Assert.AreEqual(0, state.WrongPressCount, "A correct press must cancel a same-frame wrong press.");
        Assert.AreEqual(1, state.Cursor);
    }

    [Test]
    public void WrongPress_AloneInAFrame_IsStillPenalized()
    {
        SequenceState state = NewState(Instant());
        state.StartSequence(new[] { Orange, Blue });

        PressAndTick(state, Player2);

        Assert.AreEqual(1, state.WrongPressCount);
    }

    // --- Completion and timeout -----------------------------------------

    [Test]
    public void SequenceComplete_RaisesOnceWithTheWrongPressCount()
    {
        SequenceState state = NewState(Instant());
        int completions = 0;
        int reportedWrong = -1;
        state.OnSequenceComplete += wrong => { completions++; reportedWrong = wrong; };
        state.StartSequence(new[] { Orange, Blue });

        PressAndTick(state, Player2); // wrong on slot 0
        PressAndTick(state, Player1); // correct on slot 0
        state.Press(Player2);         // correct on slot 1

        Assert.AreEqual(1, completions);
        Assert.AreEqual(1, reportedWrong);
        Assert.AreEqual(1, state.CompletedCount);
        Assert.IsFalse(state.IsActive);
    }

    [Test]
    public void Timeout_FiresOnceWhenTheClockRunsOut()
    {
        SequenceSettings settings = Instant();
        settings.SequenceSeconds = 0.5f;

        SequenceState state = NewState(settings);
        int timeouts = 0;
        state.OnTimeout += () => timeouts++;
        state.StartSequence(new[] { Orange, Blue });

        state.Tick(0.4f);
        Assert.AreEqual(0, timeouts);

        state.Tick(0.2f);
        Assert.AreEqual(1, timeouts);
        Assert.IsFalse(state.IsActive);

        // Further ticks must not re-raise it.
        state.Tick(1f);
        Assert.AreEqual(1, timeouts);
    }

    [Test]
    public void SequenceNumber_IncrementsPerSequence()
    {
        SequenceState state = NewState(Instant());

        state.StartSequence(new[] { Orange });
        Assert.AreEqual(1, state.SequenceNumber);

        state.StartSequence(new[] { Blue });
        Assert.AreEqual(2, state.SequenceNumber);
    }

    [Test]
    public void RestartSequence_ReplaysTheSameRowWithoutCountingItAsANewOne()
    {
        SequenceState state = NewState(Instant());
        state.StartSequence(new[] { Orange, OrangeSwap, Orange });

        // Get partway in and flip the mapping, so a stale swap would show up.
        PressAndTick(state, Player1);
        PressAndTick(state, Player1);
        Assert.AreEqual(2, state.Cursor);
        Assert.IsTrue(state.IsSwapped);

        state.RestartSequence();

        Assert.AreEqual(0, state.Cursor, "A retry starts the row again from the top.");
        Assert.IsFalse(state.IsSwapped, "A retry clears the swap the failed attempt left behind.");
        Assert.IsTrue(state.IsActive);
        Assert.AreEqual(1, state.SequenceNumber, "A retry is the same sequence, so the number must not advance.");
        CollectionAssert.AreEqual(new[] { Orange, OrangeSwap, Orange }, state.Tokens);
    }

    [Test]
    public void RestartSequence_RestoresTheOrderAShuffleChanged()
    {
        SequenceState state = NewState(Instant());
        SequenceToken[] original = { OrangeShuffle, Blue, Orange, Blue, Orange };
        state.StartSequence(original);

        PressAndTick(state, Player1);

        state.RestartSequence();

        CollectionAssert.AreEqual(original, state.Tokens, "A retry deals the row as it was first dealt.");
    }

    [Test]
    public void RestartSequence_DoesNothingBeforeAnythingHasBeenDealt()
    {
        SequenceState state = NewState(Instant());

        state.RestartSequence();

        Assert.AreEqual(0, state.SequenceNumber);
        Assert.IsFalse(state.IsActive);
    }

    [Test]
    public void StartSequence_CopiesTheCallersArray()
    {
        SequenceState state = NewState(Instant());
        SequenceToken[] buffer = { Orange, Blue };
        state.StartSequence(buffer);

        buffer[0] = Blue;

        Assert.AreEqual(Orange, state.Tokens[0], "The state must not alias the caller's buffer.");
    }
}
