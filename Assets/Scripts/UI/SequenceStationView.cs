using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

/// <summary>
/// The presentation for one team's station: the row of tokens, the cursor
/// marker, the draining timer bar and the score.
/// <para>
/// It subscribes to a <see cref="SequenceState"/> and never decides anything —
/// every rule lives in the pure logic. Audio and extra juice hang off the
/// UnityEvents so they can be wired in the inspector without code.
/// </para>
/// </summary>
public class SequenceStationView : MonoBehaviour
{
    private const float ShuffleMoveSeconds = 0.28f;

    [Header("Layout")]
    [Tooltip("Parent the token views are spawned under.")]
    [SerializeField] private RectTransform _tokenRow;
    [Tooltip("Prefab for a single token.")]
    [SerializeField] private SequenceTokenView _tokenPrefab;
    [Tooltip("Horizontal distance between token centres.")]
    [SerializeField] private float _tokenSpacing = 120f;

    [Header("Markers")]
    [Tooltip("Marker that sits above the current token.")]
    [SerializeField] private RectTransform _cursor;
    [Tooltip("How far above the row the cursor marker sits.")]
    [SerializeField] private float _cursorHeight = 90f;
    [Tooltip("How quickly the cursor slides between tokens. 0 snaps.")]
    [SerializeField] private float _cursorSmoothing = 20f;

    [Header("Readouts")]
    [Tooltip("Image used as the round's draining timer bar. It is stretched across its parent, and its right edge tracks the time left.")]
    [SerializeField] private Image _timerBar;
    [Tooltip("Seconds left at which the bar turns red and starts shaking.")]
    [SerializeField, Min(0f)] private float _timerWarningSeconds = 5f;
    [SerializeField] private Color _timerWarningColor = new Color(0.9f, 0.18f, 0.15f);
    [Tooltip("How far the bar shakes when time is nearly up, in canvas units.")]
    [SerializeField, Min(0f)] private float _timerShakeAmplitude = 7f;
    [SerializeField, Min(0f)] private float _timerShakeFrequency = 38f;
    [SerializeField] private TextMeshProUGUI _scoreLabel;
    [Tooltip("Optional hint shown while the mapping is flipped. Hidden unless the config turns it on.")]
    [SerializeField] private GameObject _swappedIndicator;

    [Header("Palette")]
    [SerializeField] private SequenceTokenPalette _palette;

    [Header("Feedback Hooks")]
    [SerializeField] private UnityEvent _onTokenCompleted;
    [SerializeField] private UnityEvent _onWrongPress;
    [SerializeField] private UnityEvent _onSwap;
    [SerializeField] private UnityEvent _onShuffle;
    [SerializeField] private UnityEvent _onSequenceComplete;
    [SerializeField] private UnityEvent _onTimeout;

    private readonly List<SequenceTokenView> _views = new List<SequenceTokenView>();
    private readonly List<SequenceTokenView> _pool = new List<SequenceTokenView>();

    private SequenceState _state;
    private bool _showSwappedIndicator;
    private Vector2 _cursorTarget;
    private bool _warnedMissingRefs;

    // A non-positive duration means nobody outside is driving the bar, so it
    // falls back to the sequence's own clock — all the standalone test scene has.
    private float _externalSecondsRemaining;
    private float _externalDurationSeconds = -1f;

    private RectTransform _timerFrame;
    private Vector2 _timerFrameHome;
    private Color _timerColor;

    /// <summary>The state this station is currently showing, if any.</summary>
    public SequenceState State => _state;

    private void Awake()
    {
        if (_swappedIndicator != null)
        {
            _swappedIndicator.SetActive(false);
        }

        if (_timerBar != null)
        {
            _timerColor = _timerBar.color;
            _timerFrame = _timerBar.rectTransform.parent as RectTransform;
            if (_timerFrame != null)
            {
                _timerFrameHome = _timerFrame.anchoredPosition;
            }

            // The fill is sized by its anchors rather than Image.fillAmount, so
            // a plain square-edged bar works without needing a sprite.
            RectTransform fill = _timerBar.rectTransform;
            fill.anchorMin = Vector2.zero;
            fill.anchorMax = Vector2.one;
            fill.pivot = new Vector2(0f, 0.5f);
            fill.offsetMin = Vector2.zero;
            fill.offsetMax = Vector2.zero;
            _timerBar.type = Image.Type.Simple;
        }
    }

    private void OnDestroy()
    {
        Unbind();
    }

    private void Update()
    {
        UpdateTimerBar();

        if (_state == null || _cursor == null)
        {
            return;
        }

        // Smoothing is frame-rate independent so the marker feels the same at any fps.
        _cursor.anchoredPosition = _cursorSmoothing <= 0f
            ? _cursorTarget
            : Vector2.Lerp(_cursor.anchoredPosition, _cursorTarget, 1f - Mathf.Exp(-_cursorSmoothing * Time.deltaTime));
    }

    /// <summary>
    /// Points the timer bar at an outside clock. The cooking round's timer
    /// drives it this way; a non-positive duration hands the bar back to the
    /// sequence's own countdown.
    /// </summary>
    public void SetRoundTimer(float secondsRemaining, float durationSeconds)
    {
        _externalSecondsRemaining = secondsRemaining;
        _externalDurationSeconds = durationSeconds;
    }

    /// <summary>
    /// Drains the bar from the right, and once time is nearly up tints it red and
    /// shakes its frame. The round's clock owns the bar wherever one exists, so
    /// it keeps draining across deals instead of snapping back each row.
    /// </summary>
    private void UpdateTimerBar()
    {
        if (_timerBar == null)
        {
            return;
        }

        float remaining;
        float fraction;
        if (_externalDurationSeconds > 0f)
        {
            remaining = Mathf.Max(0f, _externalSecondsRemaining);
            fraction = remaining / _externalDurationSeconds;
        }
        else
        {
            remaining = _state != null ? _state.TimeRemaining : 0f;
            fraction = _state != null ? _state.TimeFraction : 0f;
        }

        RectTransform fill = _timerBar.rectTransform;
        fill.anchorMax = new Vector2(Mathf.Clamp01(fraction), 1f);

        // An empty bar has nothing left to warn about, so it sits still.
        bool warning = remaining > 0f && remaining <= _timerWarningSeconds;
        _timerBar.color = warning ? _timerWarningColor : _timerColor;

        if (_timerFrame == null)
        {
            return;
        }

        if (!warning)
        {
            _timerFrame.anchoredPosition = _timerFrameHome;
            return;
        }

        // Shakes harder as the last seconds run out.
        float urgency = 1f - (remaining / Mathf.Max(0.01f, _timerWarningSeconds));
        float amplitude = _timerShakeAmplitude * Mathf.Lerp(0.5f, 1f, urgency);
        float t = Time.time * _timerShakeFrequency;
        var offset = new Vector2(
            (Mathf.PerlinNoise(t, 0.3f) - 0.5f) * 2f * amplitude,
            (Mathf.PerlinNoise(0.7f, t) - 0.5f) * 2f * amplitude * 0.5f);
        _timerFrame.anchoredPosition = _timerFrameHome + offset;
    }

    /// <summary>Attaches this station to a team's sequence state.</summary>
    public void Bind(SequenceState state, bool showSwappedIndicator)
    {
        Unbind();

        _state = state;
        _showSwappedIndicator = showSwappedIndicator;

        if (_state == null)
        {
            return;
        }

        _state.OnSequenceStarted += HandleSequenceStarted;
        _state.OnTokenCompleted += HandleTokenCompleted;
        _state.OnWrongPress += HandleWrongPress;
        _state.OnSwap += HandleSwap;
        _state.OnShuffle += HandleShuffle;
        _state.OnCursorMoved += HandleCursorMoved;
        _state.OnSequenceComplete += HandleSequenceComplete;
        _state.OnTimeout += HandleTimeout;
        _state.OnSequenceReset += HandleSequenceReset;

        // A state may already be mid-sequence when the view is attached.
        if (_state.Tokens.Count > 0)
        {
            RebuildRow();
            HandleCursorMoved(_state.Cursor);
        }

        UpdateSwappedIndicator();
        UpdateLabels();
    }

    /// <summary>Detaches from the current state, leaving the row as it stands.</summary>
    public void Unbind()
    {
        if (_state == null)
        {
            return;
        }

        _state.OnSequenceStarted -= HandleSequenceStarted;
        _state.OnTokenCompleted -= HandleTokenCompleted;
        _state.OnWrongPress -= HandleWrongPress;
        _state.OnSwap -= HandleSwap;
        _state.OnShuffle -= HandleShuffle;
        _state.OnCursorMoved -= HandleCursorMoved;
        _state.OnSequenceComplete -= HandleSequenceComplete;
        _state.OnTimeout -= HandleTimeout;
        _state.OnSequenceReset -= HandleSequenceReset;
        _state = null;
    }

    private void HandleSequenceStarted(IReadOnlyList<SequenceToken> tokens)
    {
        RebuildRow();
        UpdateLabels();
        UpdateSwappedIndicator();
    }

    private void HandleSequenceReset()
    {
        // The original order was restored, so redraw rather than animate.
        RebuildRow();
        UpdateSwappedIndicator();
    }

    private void HandleTokenCompleted(int slot, int playerIndex)
    {
        if (slot >= 0 && slot < _views.Count)
        {
            _views[slot].SetCompleted(true, _palette);
            _views[slot].PlayComplete();
        }

        _onTokenCompleted?.Invoke();
    }

    private void HandleWrongPress(int offender, int expected)
    {
        int slot = _state != null ? _state.Cursor : -1;
        if (slot >= 0 && slot < _views.Count)
        {
            _views[slot].PlayWrong(_palette);
        }

        _onWrongPress?.Invoke();
    }

    private void HandleSwap(bool isSwapped)
    {
        UpdateSwappedIndicator();
        _onSwap?.Invoke();
    }

    /// <summary>
    /// Reorders the spawned views to match the new slot order and slides each
    /// one into place, so players can see which tokens moved where.
    /// </summary>
    private void HandleShuffle(int[] oldOrder, int[] newOrder)
    {
        if (newOrder == null || _views.Count == 0)
        {
            return;
        }

        var reordered = new List<SequenceTokenView>(_views.Count);
        for (int slot = 0; slot < newOrder.Length; slot++)
        {
            SequenceTokenView view = FindViewById(newOrder[slot]);
            if (view != null)
            {
                reordered.Add(view);
            }
        }

        if (reordered.Count != _views.Count)
        {
            // Ids didn't line up; fall back to a clean redraw rather than lie.
            RebuildRow();
            return;
        }

        _views.Clear();
        _views.AddRange(reordered);

        for (int slot = 0; slot < _views.Count; slot++)
        {
            _views[slot].MoveTo(SlotPosition(slot), ShuffleMoveSeconds);
        }

        _onShuffle?.Invoke();
    }

    private void HandleCursorMoved(int index)
    {
        _cursorTarget = SlotPosition(index) + new Vector2(0f, _cursorHeight);

        if (_cursor != null && !_cursor.gameObject.activeSelf)
        {
            _cursor.gameObject.SetActive(true);
        }
    }

    private void HandleSequenceComplete(int wrongPresses)
    {
        UpdateLabels();
        _onSequenceComplete?.Invoke();
    }

    private void HandleTimeout()
    {
        UpdateLabels();
        _onTimeout?.Invoke();
    }

    private SequenceTokenView FindViewById(int tokenId)
    {
        for (int i = 0; i < _views.Count; i++)
        {
            if (_views[i].TokenId == tokenId)
            {
                return _views[i];
            }
        }

        return null;
    }

    /// <summary>Respawns the whole row from the state's current tokens.</summary>
    private void RebuildRow()
    {
        if (_state == null)
        {
            return;
        }

        // Without these the station renders a cursor and timer over an empty
        // row, which looks like the mechanic is broken rather than unwired.
        if (_tokenRow == null || _tokenPrefab == null)
        {
            if (!_warnedMissingRefs)
            {
                _warnedMissingRefs = true;
                Debug.LogError(
                    $"{name}: Token Row or Token Prefab is not assigned, so no tokens can be shown. " +
                    "Re-run Waffle Party > Create Sequence Test Scene (or Upgrade Cook And Decorate Scene To Sequences).",
                    this);
            }

            return;
        }

        IReadOnlyList<SequenceToken> tokens = _state.Tokens;
        IReadOnlyList<int> ids = _state.TokenIds;

        EnsureViewCount(tokens.Count);

        for (int i = 0; i < tokens.Count; i++)
        {
            SequenceTokenView view = _views[i];
            view.gameObject.SetActive(true);
            view.Bind(tokens[i], ids[i], _palette);
            view.SnapTo(SlotPosition(i));
            view.SetCompleted(i < _state.Cursor, _palette);
        }

        HandleCursorMoved(_state.Cursor);

        if (_cursor != null)
        {
            // Redraws should not have the marker fly in from its old slot.
            _cursor.anchoredPosition = _cursorTarget;
        }
    }

    /// <summary>Grows or shrinks the active view list, reusing pooled instances.</summary>
    private void EnsureViewCount(int count)
    {
        for (int i = _views.Count; i < count; i++)
        {
            _views.Add(TakeFromPool());
        }

        for (int i = _views.Count - 1; i >= count; i--)
        {
            SequenceTokenView view = _views[i];
            _views.RemoveAt(i);
            view.gameObject.SetActive(false);
            _pool.Add(view);
        }
    }

    private SequenceTokenView TakeFromPool()
    {
        if (_pool.Count > 0)
        {
            SequenceTokenView pooled = _pool[_pool.Count - 1];
            _pool.RemoveAt(_pool.Count - 1);
            return pooled;
        }

        return Instantiate(_tokenPrefab, _tokenRow);
    }

    private Vector2 SlotPosition(int index)
    {
        int count = Mathf.Max(1, _views.Count);

        // Centre the row on the container regardless of length.
        float offset = (count - 1) * 0.5f;
        float clamped = Mathf.Clamp(index, 0, count - 1);
        return new Vector2((clamped - offset) * _tokenSpacing, 0f);
    }

    private void UpdateLabels()
    {
        if (_state == null)
        {
            return;
        }

        if (_scoreLabel != null)
        {
            _scoreLabel.text = _state.CompletedCount.ToString();
        }
    }

    private void UpdateSwappedIndicator()
    {
        if (_swappedIndicator == null)
        {
            return;
        }

        bool visible = _showSwappedIndicator && _state != null && _state.IsSwapped;
        _swappedIndicator.SetActive(visible);
    }
}
