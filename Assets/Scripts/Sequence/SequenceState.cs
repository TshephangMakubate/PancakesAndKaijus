using System;
using System.Collections.Generic;

/// <summary>
/// One team's live sequence: the token row, the cursor, the swap flag and the
/// clock. Pure C# — it has no Unity dependency and is driven entirely by
/// <see cref="Press"/> and <see cref="Tick"/>, raising events that a view layer
/// subscribes to. Two teams each own an independent instance.
/// <para>
/// The rule the whole mechanic turns on is <see cref="ExpectedPlayer"/>:
/// a token's owning player is its channel owner XOR the team's swap flag.
/// </para>
/// </summary>
public class SequenceState
{
    private readonly Random _random;

    private SequenceSettings _settings;
    private SequenceToken[] _tokens = Array.Empty<SequenceToken>();
    private SequenceToken[] _originalTokens = Array.Empty<SequenceToken>();
    private int[] _tokenIds = Array.Empty<int>();
    private int[] _originalTokenIds = Array.Empty<int>();

    private int _cursor;
    private bool _isSwapped;
    private float _timeRemaining;
    private float _actionDelay;
    private bool _active;

    // Same-frame arbitration. A wrong press is held until the next Tick so a
    // teammate's correct press in the same frame can cancel it.
    private bool _correctPressThisFrame;
    private int _pendingWrongPlayer = -1;

    /// <summary>Raised when a fresh sequence is dealt.</summary>
    public event Action<IReadOnlyList<SequenceToken>> OnSequenceStarted;

    /// <summary>Raised when a button token is correctly pressed. Carries the slot and the player.</summary>
    public event Action<int, int> OnTokenCompleted;

    /// <summary>Raised when the wrong player presses. Carries the offending player and the expected one.</summary>
    public event Action<int, int> OnWrongPress;

    /// <summary>Raised when a swap token resolves. Carries the new swap state.</summary>
    public event Action<bool> OnSwap;

    /// <summary>Raised when a shuffle resolves, with token ids in slot order before and after.</summary>
    public event Action<int[], int[]> OnShuffle;

    /// <summary>Raised when the cursor moves. Carries the new slot index.</summary>
    public event Action<int> OnCursorMoved;

    /// <summary>Raised when the whole sequence is finished. Carries the wrong-press count.</summary>
    public event Action<int> OnSequenceComplete;

    /// <summary>Raised when the clock runs out before the sequence was finished.</summary>
    public event Action OnTimeout;

    /// <summary>Raised when a penalty sent the cursor back to the start.</summary>
    public event Action OnSequenceReset;

    /// <summary>Creates a state machine with its own deterministic shuffle source.</summary>
    public SequenceState(SequenceSettings settings, Random random)
    {
        _settings = settings.Validated();
        _random = random ?? new Random();
    }

    /// <summary>The tokens in their current slot order.</summary>
    public IReadOnlyList<SequenceToken> Tokens => _tokens;

    /// <summary>Stable per-token ids in slot order, so a view can follow a token across a shuffle.</summary>
    public IReadOnlyList<int> TokenIds => _tokenIds;

    /// <summary>Slot the cursor is on.</summary>
    public int Cursor => _cursor;

    /// <summary>True while the color-to-player mapping is flipped.</summary>
    public bool IsSwapped => _isSwapped;

    /// <summary>Seconds left on this sequence's clock.</summary>
    public float TimeRemaining => _timeRemaining;

    /// <summary>Clock as a 0..1 fraction, for a drain bar.</summary>
    public float TimeFraction => _settings.SequenceSeconds <= 0f ? 0f : _timeRemaining / _settings.SequenceSeconds;

    /// <summary>True while a sequence is running and accepting input.</summary>
    public bool IsActive => _active;

    /// <summary>True while an action is playing out and input is being ignored.</summary>
    public bool IsResolvingAction => _actionDelay > 0f;

    /// <summary>Wrong presses accumulated in the current sequence.</summary>
    public int WrongPressCount { get; private set; }

    /// <summary>Sequences this team has completed, used for the score and the INPUT counter.</summary>
    public int CompletedCount { get; private set; }

    /// <summary>1-based number of the sequence being played, for the "INPUT 03" label.</summary>
    public int SequenceNumber { get; private set; }

    /// <summary>The settings in force. Replacing them takes effect on the next sequence.</summary>
    public SequenceSettings Settings
    {
        get => _settings;
        set => _settings = value.Validated();
    }

    /// <summary>Which player owns a channel before any swap is applied.</summary>
    public static int OwnerOf(TokenChannel channel)
    {
        return channel == TokenChannel.Orange ? 0 : 1;
    }

    /// <summary>
    /// The player who must press for this token right now:
    /// the channel's owner XOR the team's swap flag.
    /// </summary>
    public int ExpectedPlayer(in SequenceToken token)
    {
        int owner = OwnerOf(token.Channel);
        return _isSwapped ? 1 - owner : owner;
    }

    /// <summary>The player who must press for the token under the cursor, or -1 if none.</summary>
    public int ExpectedPlayerAtCursor()
    {
        if (!_active || _cursor < 0 || _cursor >= _tokens.Length)
        {
            return -1;
        }

        return ExpectedPlayer(_tokens[_cursor]);
    }

    /// <summary>
    /// Deals a new sequence, resetting the cursor, swap flag and clock.
    /// The array is copied, so the caller may reuse its buffer.
    /// </summary>
    public void StartSequence(SequenceToken[] tokens)
    {
        Begin(tokens, true);
    }

    /// <summary>
    /// Re-deals the sequence that was just being played, from the top. It is the
    /// same row rather than the next one, so a team that ran out of time stays on
    /// the row it failed instead of being handed a new one.
    /// </summary>
    public void RestartSequence()
    {
        if (_originalTokens.Length > 0)
        {
            Begin(_originalTokens, false);
        }
    }

    /// <summary>
    /// Puts a row into play. <paramref name="isNewSequence"/> is what separates a
    /// fresh deal from a retry: only a fresh deal advances the sequence number,
    /// so a retry neither counts towards the difficulty ramp nor skips a row.
    /// </summary>
    private void Begin(SequenceToken[] tokens, bool isNewSequence)
    {
        if (tokens == null)
        {
            throw new ArgumentNullException(nameof(tokens));
        }

        _tokens = (SequenceToken[])tokens.Clone();
        _originalTokens = (SequenceToken[])tokens.Clone();

        _tokenIds = new int[_tokens.Length];
        _originalTokenIds = new int[_tokens.Length];
        for (int i = 0; i < _tokens.Length; i++)
        {
            _tokenIds[i] = i;
            _originalTokenIds[i] = i;
        }

        _cursor = 0;
        _isSwapped = false;
        _timeRemaining = _settings.SequenceSeconds;
        _actionDelay = 0f;
        _active = _tokens.Length > 0;
        WrongPressCount = 0;
        ClearFrameLatches();

        if (isNewSequence)
        {
            SequenceNumber++;
        }

        OnSequenceStarted?.Invoke(_tokens);
        OnCursorMoved?.Invoke(_cursor);
    }

    /// <summary>Stops the current sequence without raising completion or timeout.</summary>
    public void Stop()
    {
        _active = false;
        ClearFrameLatches();
    }

    /// <summary>
    /// Registers a press from one of the team's two players (0 or 1). Every
    /// tile is pressable: a tile carrying an action fires it on a correct
    /// press. Presses are ignored while an action is playing out and once the
    /// sequence is over. A wrong press is held until the next
    /// <see cref="Tick"/> so a teammate's correct press can cancel it.
    /// <para>
    /// At most one token resolves per frame: once a correct press lands, any
    /// further press that frame is dropped. That is what makes a simultaneous
    /// press safe for the losing player, and it caps the team at one token per
    /// frame — far above what two people can actually hit at 60Hz.
    /// </para>
    /// </summary>
    public void Press(int playerIndex)
    {
        if (!_active || _actionDelay > 0f)
        {
            return;
        }

        if (_cursor < 0 || _cursor >= _tokens.Length)
        {
            return;
        }

        // A teammate already resolved this slot this frame; ignore the loser.
        if (_correctPressThisFrame)
        {
            return;
        }

        SequenceToken token = _tokens[_cursor];
        int expected = ExpectedPlayer(token);

        if (playerIndex != expected)
        {
            // Hold it: the right player may still press before the frame ends.
            if (_pendingWrongPlayer < 0)
            {
                _pendingWrongPlayer = playerIndex;
            }

            return;
        }

        // Correct press wins the frame outright and cancels any pending mistake.
        _correctPressThisFrame = true;
        _pendingWrongPlayer = -1;

        OnTokenCompleted?.Invoke(_cursor, playerIndex);

        // An action tile fires its effect as it is pressed, then holds the
        // cursor briefly so the swap or shuffle can be seen before moving on.
        if (token.HasAction)
        {
            ApplyAction(token.Action);

            _actionDelay = _settings.ModifierResolveDelay;
            if (_actionDelay > 0f)
            {
                return;
            }
        }

        AdvanceCursor();
    }

    /// <summary>
    /// Advances time: settles any held wrong press, resolves modifiers under
    /// the cursor, then drains the clock.
    /// </summary>
    public void Tick(float deltaTime)
    {
        if (!_active)
        {
            return;
        }

        SettlePendingWrongPress();

        if (!_active)
        {
            return;
        }

        if (_actionDelay > 0f)
        {
            _actionDelay -= deltaTime;
            if (_actionDelay <= 0f)
            {
                _actionDelay = 0f;
                AdvanceCursor();
            }
        }

        if (!_active || _settings.NoSequenceClock)
        {
            return;
        }

        _timeRemaining -= deltaTime;
        if (_timeRemaining <= 0f)
        {
            _timeRemaining = 0f;
            _active = false;

            if (_settings.Timeout == TimeoutConsequence.NewSequenceAndScorePenalty && CompletedCount > 0)
            {
                CompletedCount--;
            }

            OnTimeout?.Invoke();
        }
    }

    /// <summary>Debug hook: flips the mapping as though a swap token resolved.</summary>
    public void ForceSwap()
    {
        ApplySwap();
    }

    /// <summary>Debug hook: rearranges the pending button tokens as though a shuffle resolved.</summary>
    public void ForceShuffle()
    {
        ApplyShuffle();
    }

    private void ClearFrameLatches()
    {
        _correctPressThisFrame = false;
        _pendingWrongPlayer = -1;
    }

    /// <summary>Applies a wrong press that was not cancelled by a teammate this frame.</summary>
    private void SettlePendingWrongPress()
    {
        int offender = _pendingWrongPlayer;
        ClearFrameLatches();

        if (offender < 0)
        {
            return;
        }

        WrongPressCount++;
        OnWrongPress?.Invoke(offender, 1 - offender);

        if (_settings.WrongPressPenalty == WrongPressPenaltyMode.StayOnToken)
        {
            return;
        }

        if (_settings.WrongPressPenalty == WrongPressPenaltyMode.TimeDeduction)
        {
            _timeRemaining -= _settings.WrongPressSeconds;

            // Let the normal clock drain in Tick raise the timeout.
            if (_timeRemaining < 0f)
            {
                _timeRemaining = 0f;
            }

            return;
        }

        ResetToStart();
    }

    /// <summary>
    /// Sends the cursor home with the swap cleared and the original token order
    /// restored — the harsher of the two configurable penalties.
    /// </summary>
    private void ResetToStart()
    {
        _tokens = (SequenceToken[])_originalTokens.Clone();
        _tokenIds = (int[])_originalTokenIds.Clone();
        _cursor = 0;
        _actionDelay = 0f;

        if (_isSwapped)
        {
            _isSwapped = false;
            OnSwap?.Invoke(false);
        }

        OnSequenceReset?.Invoke();
        OnCursorMoved?.Invoke(_cursor);
    }

    /// <summary>Dispatches an action to its effect. New actions slot in here.</summary>
    private void ApplyAction(TokenAction action)
    {
        switch (action)
        {
            case TokenAction.Swap:
                ApplySwap();
                break;
            case TokenAction.Shuffle:
                ApplyShuffle();
                break;
        }
    }

    /// <summary>Flips the color-to-player mapping for the rest of the sequence.</summary>
    private void ApplySwap()
    {
        _isSwapped = !_isSwapped;
        OnSwap?.Invoke(_isSwapped);
    }

    /// <summary>
    /// Randomly reorders the tiles still to come. A tile carries its action
    /// with it, so both the colours and any upcoming actions land in new
    /// places. Tiles already resolved, and the action tile being pressed, never
    /// move.
    /// </summary>
    private void ApplyShuffle()
    {
        var slots = new List<int>();
        for (int i = _cursor + 1; i < _tokens.Length; i++)
        {
            slots.Add(i);
        }

        if (slots.Count < 2)
        {
            return;
        }

        int[] oldIds = (int[])_tokenIds.Clone();

        var tokens = new SequenceToken[slots.Count];
        var ids = new int[slots.Count];
        for (int i = 0; i < slots.Count; i++)
        {
            tokens[i] = _tokens[slots[i]];
            ids[i] = _tokenIds[slots[i]];
        }

        bool canDiffer = _settings.GuaranteeShuffleChangesOrder && HasDistinctTokens(tokens);

        // Without distinct tokens any permutation looks identical, so one pass
        // is all that is warranted.
        int attempts = canDiffer ? 8 : 1;
        var shuffledTokens = new SequenceToken[tokens.Length];
        var shuffledIds = new int[ids.Length];

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            Array.Copy(tokens, shuffledTokens, tokens.Length);
            Array.Copy(ids, shuffledIds, ids.Length);

            for (int i = shuffledTokens.Length - 1; i > 0; i--)
            {
                int j = _random.Next(i + 1);
                (shuffledTokens[i], shuffledTokens[j]) = (shuffledTokens[j], shuffledTokens[i]);
                (shuffledIds[i], shuffledIds[j]) = (shuffledIds[j], shuffledIds[i]);
            }

            if (!canDiffer || !SameArrangement(tokens, shuffledTokens))
            {
                break;
            }
        }

        for (int i = 0; i < slots.Count; i++)
        {
            _tokens[slots[i]] = shuffledTokens[i];
            _tokenIds[slots[i]] = shuffledIds[i];
        }

        OnShuffle?.Invoke(oldIds, (int[])_tokenIds.Clone());
    }

    private static bool HasDistinctTokens(SequenceToken[] tokens)
    {
        for (int i = 1; i < tokens.Length; i++)
        {
            if (!tokens[i].Equals(tokens[0]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool SameArrangement(SequenceToken[] a, SequenceToken[] b)
    {
        for (int i = 0; i < a.Length; i++)
        {
            if (!a[i].Equals(b[i]))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Steps the cursor forward, completing the sequence when it runs off the end.</summary>
    private void AdvanceCursor()
    {
        _cursor++;

        if (_cursor >= _tokens.Length)
        {
            _cursor = _tokens.Length;
            _active = false;
            CompletedCount++;
            OnSequenceComplete?.Invoke(WrongPressCount);
            return;
        }

        OnCursorMoved?.Invoke(_cursor);
    }
}
