using System;
using System.Collections.Generic;

/// <summary>
/// Owns the two teams' independent <see cref="SequenceState"/> instances, deals
/// their sequences, and decides the winner. Pure C#, so the whole match rule set
/// is testable without a scene.
/// <para>
/// With <c>UseSharedSeed</c> set, team B's Nth sequence is byte-identical to
/// team A's Nth sequence, so neither side can be handed an easier row.
/// </para>
/// </summary>
public class SequenceMatch
{
    /// <summary>Teams in a 2v2 match.</summary>
    public const int TeamCount = 2;

    /// <summary>Players on each team.</summary>
    public const int PlayersPerTeam = 2;

    private readonly TokenSequenceGenerator _generator = new TokenSequenceGenerator();
    private readonly SequenceState[] _teams = new SequenceState[TeamCount];
    private readonly bool[] _teamActive = new bool[TeamCount];
    private readonly Random[] _dealRandoms = new Random[TeamCount];
    private readonly List<SequenceToken[]> _sharedSequences = new List<SequenceToken[]>();
    private readonly Random _sharedRandom;

    private SequenceSettings _settings;

    /// <summary>Creates a match whose randomness is fully determined by <paramref name="seed"/>.</summary>
    public SequenceMatch(SequenceSettings settings, int seed)
    {
        _settings = settings.Validated();
        _sharedRandom = new Random(seed);

        for (int team = 0; team < TeamCount; team++)
        {
            // Shared-seed matches give both teams the same shuffle source too,
            // so identical play produces identical rows.
            int teamSeed = _settings.UseSharedSeed ? seed : seed + team + 1;
            _teams[team] = new SequenceState(_settings, new Random(teamSeed));
            _dealRandoms[team] = new Random(teamSeed);
            _teamActive[team] = true;
        }
    }

    /// <summary>The settings in force for the match.</summary>
    public SequenceSettings Settings => _settings;

    /// <summary>Returns a team's sequence state.</summary>
    public SequenceState Team(int teamIndex)
    {
        return _teams[teamIndex];
    }

    /// <summary>True when a team is taking part; an inactive team is never ticked or dealt.</summary>
    public bool IsTeamActive(int teamIndex)
    {
        return _teamActive[teamIndex];
    }

    /// <summary>Enables or disables a team, so a 2v2 build can run with one side idle.</summary>
    public void SetTeamActive(int teamIndex, bool active)
    {
        _teamActive[teamIndex] = active;
        if (!active)
        {
            _teams[teamIndex].Stop();
        }
    }

    /// <summary>Sequences a team has completed.</summary>
    public int ScoreOf(int teamIndex)
    {
        return _teams[teamIndex].CompletedCount;
    }

    /// <summary>
    /// The team currently ahead, or -1 when the scores are level.
    /// </summary>
    public int LeadingTeam
    {
        get
        {
            int a = ScoreOf(0);
            int b = ScoreOf(1);
            if (a == b)
            {
                return -1;
            }

            return a > b ? 0 : 1;
        }
    }

    /// <summary>
    /// True when a first-to-N match has been won. Round-timer matches are never
    /// decided early — the clock in the caller ends them.
    /// </summary>
    public bool IsDecided
    {
        get
        {
            if (_settings.WinCondition != WinConditionMode.FirstToCount)
            {
                return false;
            }

            for (int team = 0; team < TeamCount; team++)
            {
                if (ScoreOf(team) >= _settings.SequencesToWin)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Deals a fresh sequence to a team, respecting the shared-seed setting and
    /// applying the difficulty ramp for that team's next sequence.
    /// </summary>
    public void DealTo(int teamIndex)
    {
        if (!_teamActive[teamIndex])
        {
            return;
        }

        SequenceState state = _teams[teamIndex];

        // SequenceNumber counts sequences already dealt, so it indexes the next.
        int index = state.SequenceNumber;
        SequenceSettings perSequence = _settings.ForSequence(index);

        // The state reads its clock from these when the sequence starts.
        state.Settings = perSequence;
        state.StartSequence(NextSequenceFor(teamIndex, index, perSequence));
    }

    /// <summary>
    /// Puts the row a team was just playing back on the board from the top,
    /// instead of moving them on to the next one.
    /// </summary>
    public void RedealTo(int teamIndex)
    {
        if (_teamActive[teamIndex])
        {
            _teams[teamIndex].RestartSequence();
        }
    }

    /// <summary>
    /// Hands all timing to an outside round clock: rows never time out, and a
    /// wrong press leaves the team on the token they missed rather than costing
    /// time or sending them back to the start.
    /// </summary>
    public void UseRoundClockOnly()
    {
        _settings.NoSequenceClock = true;
        _settings.WrongPressPenalty = WrongPressPenaltyMode.StayOnToken;

        for (int team = 0; team < TeamCount; team++)
        {
            SequenceSettings current = _teams[team].Settings;
            current.NoSequenceClock = true;
            current.WrongPressPenalty = WrongPressPenaltyMode.StayOnToken;
            _teams[team].Settings = current;
        }
    }

    /// <summary>Ticks every active team's clock.</summary>
    public void Tick(float deltaTime)
    {
        for (int team = 0; team < TeamCount; team++)
        {
            if (_teamActive[team])
            {
                _teams[team].Tick(deltaTime);
            }
        }
    }

    /// <summary>Routes a press to the right team using a flat 0..3 player index.</summary>
    public void Press(int globalPlayerIndex)
    {
        int team = globalPlayerIndex / PlayersPerTeam;
        int seat = globalPlayerIndex % PlayersPerTeam;

        if (team < 0 || team >= TeamCount || !_teamActive[team])
        {
            return;
        }

        _teams[team].Press(seat);
    }

    /// <summary>
    /// The next sequence for a team. Under a shared seed the Nth sequence is
    /// generated once and handed to both teams; otherwise each team draws from
    /// its own stream.
    /// </summary>
    private SequenceToken[] NextSequenceFor(int teamIndex, int index, SequenceSettings perSequence)
    {
        if (!_settings.UseSharedSeed)
        {
            return _generator.Generate(perSequence, _dealRandoms[teamIndex]);
        }

        // Both teams ramp identically, so the cached Nth sequence is valid for
        // either of them however far apart their pace has drifted.
        while (_sharedSequences.Count <= index)
        {
            SequenceSettings cached = _settings.ForSequence(_sharedSequences.Count);
            _sharedSequences.Add(_generator.Generate(cached, _sharedRandom));
        }

        return _sharedSequences[index];
    }
}
