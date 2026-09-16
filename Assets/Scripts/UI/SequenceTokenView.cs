using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One token in a station's row. Purely presentational: it is told what to look
/// like and where to sit, and knows nothing about the rules.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class SequenceTokenView : MonoBehaviour
{
    private const float WrongFlashSeconds = 0.18f;
    private const float CompletePopScale = 1.25f;
    private const float CompletePopSeconds = 0.12f;

    [Tooltip("The tile's coloured background box.")]
    [SerializeField] private Image _image;
    [Tooltip("Action badge drawn over the tile, shown only on tiles that carry an action.")]
    [SerializeField] private Image _icon;
    [Tooltip("Channel shape cue, so colour is never the only way to tell the two players apart.")]
    [SerializeField] private TextMeshProUGUI _glyphText;
    [Tooltip("Text badge used for an action when the palette has no icon for it.")]
    [SerializeField] private TextMeshProUGUI _badgeText;

    private RectTransform _rect;
    private Color _baseColor = Color.white;
    private Coroutine _flash;
    private Coroutine _move;

    /// <summary>Stable id of the token currently shown, so a shuffle can be followed.</summary>
    public int TokenId { get; private set; } = -1;

    /// <summary>This token's rect, for layout by the station.</summary>
    public RectTransform Rect => _rect != null ? _rect : _rect = (RectTransform)transform;

    private void Awake()
    {
        _rect = (RectTransform)transform;
    }

    /// <summary>Applies a token's look from the palette.</summary>
    public void Bind(SequenceToken token, int tokenId, SequenceTokenPalette palette)
    {
        TokenId = tokenId;

        // The tile's colour and shape always come from its channel, so an
        // action never changes what colour a tile is.
        ChannelStyle channel = palette != null ? palette.StyleFor(token.Channel) : null;
        _baseColor = channel != null ? channel.Color : Color.white;

        if (_image != null)
        {
            _image.color = _baseColor;
        }

        // The action, if any, is drawn on top: icon when the palette has one,
        // otherwise its text stand-in.
        ActionStyle badge = palette != null ? palette.BadgeFor(token.Action) : null;
        Sprite icon = badge != null ? badge.Sprite : null;

        // A plain tile is colour and nothing else. The channel glyph is an
        // optional shape cue that shares the centre with the action icon, so it
        // gives way whenever a tile carries an action.
        if (_glyphText != null)
        {
            bool showGlyph = badge == null && channel != null;
            _glyphText.text = showGlyph ? channel.Glyph : string.Empty;
        }

        if (_icon != null)
        {
            _icon.sprite = icon;
            _icon.enabled = icon != null;
        }

        if (_badgeText != null)
        {
            bool needsText = badge != null && icon == null;
            _badgeText.text = needsText ? badge.Glyph : string.Empty;
        }

        SetCompleted(false, palette);
    }

    /// <summary>Dims a token once it has been resolved.</summary>
    public void SetCompleted(bool completed, SequenceTokenPalette palette)
    {
        if (_image == null)
        {
            return;
        }

        if (!completed)
        {
            _image.color = _baseColor;
            return;
        }

        Color tint = palette != null ? palette.CompletedTint : new Color(1f, 1f, 1f, 0.3f);
        _image.color = new Color(
            _baseColor.r * tint.r,
            _baseColor.g * tint.g,
            _baseColor.b * tint.b,
            _baseColor.a * tint.a);
    }

    /// <summary>Flashes the token to acknowledge a wrong press.</summary>
    public void PlayWrong(SequenceTokenPalette palette)
    {
        if (!isActiveAndEnabled || _image == null)
        {
            return;
        }

        Color flashColor = palette != null ? palette.WrongFlashColor : Color.red;

        if (_flash != null)
        {
            StopCoroutine(_flash);
        }

        _flash = StartCoroutine(FlashRoutine(flashColor));
    }

    /// <summary>Pops the token to acknowledge a correct press.</summary>
    public void PlayComplete()
    {
        if (!isActiveAndEnabled)
        {
            return;
        }

        StartCoroutine(PopRoutine());
    }

    /// <summary>Slides the token to a new slot, used for the shuffle animation.</summary>
    public void MoveTo(Vector2 anchoredPosition, float duration)
    {
        if (!isActiveAndEnabled || duration <= 0f)
        {
            Rect.anchoredPosition = anchoredPosition;
            return;
        }

        if (_move != null)
        {
            StopCoroutine(_move);
        }

        _move = StartCoroutine(MoveRoutine(anchoredPosition, duration));
    }

    /// <summary>Places the token immediately, cancelling any slide in flight.</summary>
    public void SnapTo(Vector2 anchoredPosition)
    {
        if (_move != null)
        {
            StopCoroutine(_move);
            _move = null;
        }

        Rect.anchoredPosition = anchoredPosition;
    }

    private IEnumerator FlashRoutine(Color flashColor)
    {
        Color from = _image.color;
        _image.color = flashColor;
        yield return new WaitForSeconds(WrongFlashSeconds);
        _image.color = from;
        _flash = null;
    }

    private IEnumerator PopRoutine()
    {
        Vector3 baseScale = Vector3.one;
        float elapsed = 0f;

        while (elapsed < CompletePopSeconds)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / CompletePopSeconds);
            // Out and back, so the token settles at its authored size.
            float scale = Mathf.Lerp(CompletePopScale, 1f, t);
            Rect.localScale = baseScale * scale;
            yield return null;
        }

        Rect.localScale = baseScale;
    }

    private IEnumerator MoveRoutine(Vector2 target, float duration)
    {
        Vector2 from = Rect.anchoredPosition;
        float elapsed = 0f;

        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            // Smoothstep so the swap reads as deliberate rather than linear.
            t = t * t * (3f - 2f * t);
            Rect.anchoredPosition = Vector2.Lerp(from, target, t);
            yield return null;
        }

        Rect.anchoredPosition = target;
        _move = null;
    }
}
