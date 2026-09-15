using TMPro;
using UnityEngine;

/// <summary>
/// Drives on-screen text: current round, countdown timer, waffles this round,
/// and running accuracy, plus a final results panel.
/// </summary>
public class HUDController : MonoBehaviour
{
    private const float BannerPopSeconds = 0.3f;

    [Header("HUD Text")]
    [SerializeField] private TextMeshProUGUI _roundText;
    [SerializeField] private TextMeshProUGUI _timerText;
    [SerializeField] private TextMeshProUGUI _waffleCountText;
    [SerializeField] private TextMeshProUGUI _accuracyText;

    [Header("Results")]
    [SerializeField] private GameObject _resultsPanel;
    [SerializeField] private TextMeshProUGUI _resultsText;

    private Vector3 _resultsPanelScale = Vector3.one;

    private void Awake()
    {
        if (_resultsPanel != null)
        {
            _resultsPanelScale = _resultsPanel.transform.localScale;
            _resultsPanel.SetActive(false);
        }
    }

    /// <summary>Sets the current round display, 1-based for the player.</summary>
    public void SetRound(int index, int total)
    {
        if (_roundText != null)
        {
            _roundText.text = $"Round {index + 1}/{total}";
        }
    }

    /// <summary>Sets the countdown timer display in whole seconds.</summary>
    public void SetTimer(float seconds)
    {
        if (_timerText != null)
        {
            _timerText.text = $"{Mathf.CeilToInt(Mathf.Max(0f, seconds))}s";
        }
    }

    /// <summary>Sets the waffle count for the current round.</summary>
    public void SetWaffleCount(int count)
    {
        if (_waffleCountText != null)
        {
            _waffleCountText.text = $"Waffles: {count}";
        }
    }

    /// <summary>Shows pancakes decorated so far and how many are left on the stack.</summary>
    public void SetDecoratedCount(int decorated, int remaining)
    {
        if (_waffleCountText != null)
        {
            _waffleCountText.text = $"Decorated: {decorated}  Left: {remaining}";
        }
    }

    /// <summary>Shows how many plates are still left to slide.</summary>
    public void SetPlatesLeft(int count)
    {
        if (_waffleCountText != null)
        {
            _waffleCountText.text = $"Plates: {count}";
        }
    }

    /// <summary>Shows the serving score.</summary>
    public void SetScore(int score)
    {
        if (_accuracyText != null)
        {
            _accuracyText.text = $"Score: {score}";
        }
    }

    /// <summary>Sets the running accuracy display as a percentage.</summary>
    public void SetAccuracy(float pct)
    {
        if (_accuracyText != null)
        {
            _accuracyText.text = $"Accuracy: {Mathf.RoundToInt(Mathf.Clamp01(pct) * 100f)}%";
        }
    }

    /// <summary>Shows a message on the results panel, e.g. between scenes.</summary>
    public void ShowMessage(string message)
    {
        if (_resultsPanel != null)
        {
            _resultsPanel.transform.localScale = _resultsPanelScale;
            _resultsPanel.SetActive(true);
        }

        if (_resultsText != null)
        {
            _resultsText.text = message;
        }
    }

    /// <summary>Pops a big announcement onto the screen, e.g. "Round 2".</summary>
    public void ShowBanner(string message)
    {
        ShowMessage(message);

        if (_resultsPanel != null)
        {
            StopAllCoroutines();
            StartCoroutine(JuiceTweens.PopIn(_resultsPanel.transform, _resultsPanelScale, BannerPopSeconds));
        }
    }

    /// <summary>Hides any message or banner.</summary>
    public void HideMessage()
    {
        StopAllCoroutines();

        if (_resultsPanel != null)
        {
            _resultsPanel.transform.localScale = _resultsPanelScale;
            _resultsPanel.SetActive(false);
        }
    }

    /// <summary>Reveals the end-of-match results for the given session.</summary>
    public void ShowResults(PlayerSession session)
    {
        ShowMessage(string.Empty);

        if (_resultsText != null && session != null)
        {
            int accuracyPct = Mathf.RoundToInt(session.CumulativeAccuracy * 100f);
            string results = $"Game Over!\nTotal Waffles: {session.TotalWaffles}\nAvg Accuracy: {accuracyPct}%";

            if (session.DecoratedCount > 0)
            {
                int decorPct = Mathf.RoundToInt(session.DecorationAccuracy * 100f);
                results += $"\nDecorated: {session.DecoratedCount}\nDecor Accuracy: {decorPct}%";
            }

            _resultsText.text = results;
        }
    }
}
