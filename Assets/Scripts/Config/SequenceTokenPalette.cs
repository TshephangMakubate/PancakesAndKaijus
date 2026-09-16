using System;
using UnityEngine;

/// <summary>
/// How one colour channel looks. Colour and shape are authored together because
/// colour alone is not an accessible way to tell two players apart.
/// </summary>
[Serializable]
public class ChannelStyle
{
    [SerializeField] private Color _color = Color.white;
    [Tooltip("Shape cue drawn on the tile, e.g. <> for a diamond. Never rely on colour alone.")]
    [SerializeField] private string _glyph = string.Empty;

    /// <summary>Tint applied to the tile.</summary>
    public Color Color => _color;

    /// <summary>Shape glyph shown alongside the colour.</summary>
    public string Glyph => _glyph;
}

/// <summary>
/// How one action is badged onto a tile. The tile keeps its channel colour, so
/// an action only ever adds a mark — it never introduces a third colour.
/// </summary>
[Serializable]
public class ActionStyle
{
    [Tooltip("Icon badged onto the tile. Leave empty to fall back to the text below.")]
    [SerializeField] private Sprite _sprite;
    [Tooltip("Text used when no icon is set.")]
    [SerializeField] private string _glyph = string.Empty;

    /// <summary>Optional icon; null falls back to <see cref="Glyph"/>.</summary>
    public Sprite Sprite => _sprite;

    /// <summary>Text badge used when there is no icon.</summary>
    public string Glyph => _glyph;
}

/// <summary>
/// Maps channels and actions to their look. The rules never reference a sprite
/// or a colour, so art can be swapped here without touching logic.
/// <para>
/// Only the two channels carry colour. Actions are badges drawn on top, which
/// is what keeps the whole row down to two colours.
/// </para>
/// </summary>
[CreateAssetMenu(fileName = "SequenceTokenPalette", menuName = "Waffle Party/Sequence Token Palette")]
public class SequenceTokenPalette : ScriptableObject
{
    [Header("Colour Channels")]
    [Tooltip("Player 1's channel before any swap.")]
    [SerializeField] private ChannelStyle _orange = new ChannelStyle();
    [Tooltip("Player 2's channel before any swap.")]
    [SerializeField] private ChannelStyle _blue = new ChannelStyle();

    [Header("Action Badges")]
    [Tooltip("Two arrows pointing opposite ways: pressing this tile flips the mapping.")]
    [SerializeField] private ActionStyle _swap = new ActionStyle();
    [Tooltip("Crossing arrows: pressing this tile rearranges the tiles still to come.")]
    [SerializeField] private ActionStyle _shuffle = new ActionStyle();

    [Header("States")]
    [SerializeField] private Color _completedTint = new Color(1f, 1f, 1f, 0.3f);
    [SerializeField] private Color _cursorColor = new Color(1f, 0.95f, 0.4f);
    [SerializeField] private Color _wrongFlashColor = new Color(1f, 0.25f, 0.2f);

    /// <summary>Tint applied to a tile that has already been resolved.</summary>
    public Color CompletedTint => _completedTint;

    /// <summary>Colour of the cursor marker.</summary>
    public Color CursorColor => _cursorColor;

    /// <summary>Flash colour for a wrong press.</summary>
    public Color WrongFlashColor => _wrongFlashColor;

    /// <summary>Look-up for a tile's colour and shape cue.</summary>
    public ChannelStyle StyleFor(TokenChannel channel)
    {
        return channel == TokenChannel.Orange ? _orange : _blue;
    }

    /// <summary>
    /// Look-up for a tile's action badge, or null when it carries none.
    /// New actions get a case here.
    /// </summary>
    public ActionStyle BadgeFor(TokenAction action)
    {
        switch (action)
        {
            case TokenAction.Swap:
                return _swap;
            case TokenAction.Shuffle:
                return _shuffle;
            default:
                return null;
        }
    }
}
