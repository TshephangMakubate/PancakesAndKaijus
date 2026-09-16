using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Debug harness for the sequence mechanic: forces a swap, a shuffle or a fresh
/// sequence on either team without waiting for one to come up naturally.
/// Intended for the test scene, but harmless to drop into any scene while
/// tuning. Remove or disable it before shipping.
/// </summary>
public class SequenceDebugKeys : MonoBehaviour
{
    [Header("Target")]
    [SerializeField] private SequenceMatchRunner _runner;

    [Header("Team A Keys")]
    [SerializeField] private Key _teamASwapKey = Key.Digit1;
    [SerializeField] private Key _teamAShuffleKey = Key.Digit2;
    [SerializeField] private Key _teamADealKey = Key.Digit3;

    [Header("Team B Keys")]
    [SerializeField] private Key _teamBSwapKey = Key.Digit8;
    [SerializeField] private Key _teamBShuffleKey = Key.Digit9;
    [SerializeField] private Key _teamBDealKey = Key.Digit0;

    [Header("Display")]
    [Tooltip("Draw an on-screen key legend while playing.")]
    [SerializeField] private bool _showLegend = true;

    private void Update()
    {
        Keyboard keyboard = Keyboard.current;
        if (keyboard == null || _runner == null)
        {
            return;
        }

        HandleTeam(keyboard, 0, _teamASwapKey, _teamAShuffleKey, _teamADealKey);
        HandleTeam(keyboard, 1, _teamBSwapKey, _teamBShuffleKey, _teamBDealKey);
    }

    private void OnGUI()
    {
        if (!_showLegend)
        {
            return;
        }

        const float width = 250f;
        const float height = 74f;
        var area = new Rect(10f, Screen.height - height - 10f, width, height);

        GUI.Box(area, "Sequence debug");
        GUI.Label(new Rect(area.x + 8f, area.y + 22f, width - 16f, 18f),
            $"Team A: {_teamASwapKey}=swap  {_teamAShuffleKey}=shuffle  {_teamADealKey}=new");
        GUI.Label(new Rect(area.x + 8f, area.y + 42f, width - 16f, 18f),
            $"Team B: {_teamBSwapKey}=swap  {_teamBShuffleKey}=shuffle  {_teamBDealKey}=new");
    }

    private void HandleTeam(Keyboard keyboard, int teamIndex, Key swapKey, Key shuffleKey, Key dealKey)
    {
        SequenceState state = _runner.Team(teamIndex);
        if (state == null)
        {
            return;
        }

        if (WasPressed(keyboard, swapKey))
        {
            state.ForceSwap();
        }

        if (WasPressed(keyboard, shuffleKey))
        {
            state.ForceShuffle();
        }

        if (WasPressed(keyboard, dealKey))
        {
            _runner.DealTo(teamIndex);
        }
    }

    private static bool WasPressed(Keyboard keyboard, Key key)
    {
        return key != Key.None && keyboard[key].wasPressedThisFrame;
    }
}
