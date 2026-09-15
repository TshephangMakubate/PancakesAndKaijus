using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Plays back the target sequence visually (arrow icons lighting up in order)
/// before the input phase, and flashes the arrow the player just pressed.
/// </summary>
public class SequenceDisplay : MonoBehaviour
{
    private const float EchoFlashSeconds = 0.15f;

    [Header("Arrow Images (Up, Down, Left, Right)")]
    [SerializeField] private Image _upArrow;
    [SerializeField] private Image _downArrow;
    [SerializeField] private Image _leftArrow;
    [SerializeField] private Image _rightArrow;

    [Header("Colors")]
    [SerializeField] private Color _idleColor = new Color(1f, 1f, 1f, 0.25f);
    [SerializeField] private Color _highlightColor = Color.white;
    [SerializeField] private Color _echoColor = new Color(0.4f, 0.9f, 1f, 1f);

    /// <summary>True while a sequence started with <see cref="Begin"/> is still playing.</summary>
    public bool IsPlaying { get; private set; }

    private void Awake()
    {
        ResetArrows();
    }

    /// <summary>Dims all arrows to their idle state.</summary>
    public void ResetArrows()
    {
        SetColor(Direction.Up, _idleColor);
        SetColor(Direction.Down, _idleColor);
        SetColor(Direction.Left, _idleColor);
        SetColor(Direction.Right, _idleColor);
    }

    /// <summary>
    /// Plays the sequence, highlighting each arrow for <paramref name="stepSeconds"/>.
    /// </summary>
    public IEnumerator Play(IReadOnlyList<Direction> sequence, float stepSeconds)
    {
        ResetArrows();

        if (sequence == null)
        {
            yield break;
        }

        float on = Mathf.Max(0.05f, stepSeconds);
        float off = Mathf.Max(0.05f, stepSeconds * 0.4f);

        for (int i = 0; i < sequence.Count; i++)
        {
            Direction d = sequence[i];
            SetColor(d, _highlightColor);
            yield return new WaitForSeconds(on);
            SetColor(d, _idleColor);
            yield return new WaitForSeconds(off);
        }
    }

    /// <summary>Starts playing the sequence without blocking, so the caller can cut it short with <see cref="Stop"/>.</summary>
    public void Begin(IReadOnlyList<Direction> sequence, float stepSeconds)
    {
        Stop();
        IsPlaying = true;
        StartCoroutine(PlayThenFinish(sequence, stepSeconds));
    }

    /// <summary>Cuts any playback short and dims the arrows.</summary>
    public void Stop()
    {
        StopAllCoroutines();
        IsPlaying = false;
        ResetArrows();
    }

    /// <summary>Flashes the arrow the player just pressed as an input echo.</summary>
    public void ShowInputEcho(Direction d)
    {
        StopAllCoroutines();
        IsPlaying = false;
        StartCoroutine(EchoRoutine(d));
    }

    private IEnumerator PlayThenFinish(IReadOnlyList<Direction> sequence, float stepSeconds)
    {
        yield return Play(sequence, stepSeconds);
        IsPlaying = false;
    }

    private IEnumerator EchoRoutine(Direction d)
    {
        SetColor(d, _echoColor);
        yield return new WaitForSeconds(EchoFlashSeconds);
        SetColor(d, _idleColor);
    }

    private void SetColor(Direction d, Color color)
    {
        Image arrow = GetArrow(d);
        if (arrow != null)
        {
            arrow.color = color;
        }
    }

    private Image GetArrow(Direction d)
    {
        switch (d)
        {
            case Direction.Up: return _upArrow;
            case Direction.Down: return _downArrow;
            case Direction.Left: return _leftArrow;
            case Direction.Right: return _rightArrow;
            default: return null;
        }
    }
}
