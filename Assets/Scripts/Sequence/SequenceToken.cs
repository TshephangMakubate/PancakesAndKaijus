using System;

/// <summary>
/// The logical colour channel a tile belongs to. Deliberately not a
/// <c>Color</c>: the rules never know what a channel looks like, so palettes
/// and art can change without touching the rules.
/// <para>
/// There are exactly two, one per player on a team, and every tile in a
/// sequence belongs to one of them.
/// </para>
/// </summary>
public enum TokenChannel
{
    Orange,
    Blue
}

/// <summary>
/// An effect a tile carries in addition to being pressed. Most tiles carry
/// <see cref="None"/>; the rest fire their effect when their owner presses
/// them. Adding a new action later (e.g. "reverse") is a new case here plus a
/// new arm in <c>SequenceState.ApplyAction</c> — not a rewrite.
/// </summary>
public enum TokenAction
{
    /// <summary>A plain tile: press it and move on.</summary>
    None,

    /// <summary>Flips which player owns which colour for the rest of the sequence.</summary>
    Swap,

    /// <summary>Randomly reorders the tiles still to come.</summary>
    Shuffle
}

/// <summary>
/// One slot in a sequence: the channel that owns it, plus an optional action
/// that fires when its owner presses it.
/// </summary>
[Serializable]
public readonly struct SequenceToken : IEquatable<SequenceToken>
{
    /// <summary>Owning colour channel. Every tile has one.</summary>
    public TokenChannel Channel { get; }

    /// <summary>Effect this tile fires when pressed, if any.</summary>
    public TokenAction Action { get; }

    private SequenceToken(TokenChannel channel, TokenAction action)
    {
        Channel = channel;
        Action = action;
    }

    /// <summary>True when pressing this tile also fires an effect.</summary>
    public bool HasAction => Action != TokenAction.None;

    /// <summary>Creates a plain tile owned by the given channel.</summary>
    public static SequenceToken Plain(TokenChannel channel)
    {
        return new SequenceToken(channel, TokenAction.None);
    }

    /// <summary>Creates a tile that fires an action when its owner presses it.</summary>
    public static SequenceToken WithAction(TokenChannel channel, TokenAction action)
    {
        return new SequenceToken(channel, action);
    }

    /// <summary>Returns this tile recoloured, keeping its action.</summary>
    public SequenceToken WithChannel(TokenChannel channel)
    {
        return new SequenceToken(channel, Action);
    }

    /// <summary>Two tiles match when their channel and action both agree.</summary>
    public bool Equals(SequenceToken other)
    {
        return Channel == other.Channel && Action == other.Action;
    }

    public override bool Equals(object obj)
    {
        return obj is SequenceToken other && Equals(other);
    }

    public override int GetHashCode()
    {
        return ((int)Channel * 397) ^ (int)Action;
    }

    public override string ToString()
    {
        return HasAction ? $"{Channel}+{Action}" : Channel.ToString();
    }
}
