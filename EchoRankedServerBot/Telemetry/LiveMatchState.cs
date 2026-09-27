using EchoRankedServerBot.Models.EchoApi;
using Nevr.Telemetry.V2;

namespace EchoRankedServerBot.Telemetry;

/// <summary>
/// Folds nevr-stream telemetry envelopes for one match into an EchoVrApiSession snapshot.
/// </summary>
public sealed class LiveMatchState
{
    private sealed class RosterEntry
    {
        public ulong AccountNumber;
        public string DisplayName = "";
        public Role Role;
        public int JerseyNumber;
        public int Level;
    }

    private sealed class DepartedPlayer
    {
        public required int Slot;
        public required RosterEntry Entry;
        public PlayerStatsUpdated? Stats;
        public PlayerState? State;
    }

    private sealed class ShotTally
    {
        public int TwoPointShots;
        public int ThreePointShots;
        public int ShortBounceShots;
        public int LongBounceShots;
        public readonly List<double?> ShotSpeed = [];
        public readonly List<double?> ThrowDistance = [];
    }

    private readonly object _lock = new();

    private readonly Dictionary<int, RosterEntry> _roster = new();
    private readonly Dictionary<int, PlayerState> _playerStates = new();
    private readonly Dictionary<int, PlayerStatsUpdated> _stats = new();
    // Players leave within seconds of post_match, so their last stats are kept for recording.
    private readonly Dictionary<ulong, DepartedPlayer> _departed = new();
    private readonly Dictionary<ulong, ShotTally> _shots = new();

    private string _sessionId = "";
    private string _mapName = "";
    private string _blueTeamName = "BLUE TEAM";
    private string _orangeTeamName = "ORANGE TEAM";
    private int _totalRoundCount;

    private GameStatus _gameStatus;
    private float _gameClock;
    private string _gameClockDisplay = "";
    private int _bluePoints;
    private int _orangePoints;
    private int _blueRoundScore;
    private int _orangeRoundScore;
    private PauseState _pauseState;
    private PauseDetail? _pauseDetail;
    private LastScoreInfo? _lastScore;

    public bool HasData { get; private set; }
    public bool Ended { get; private set; }

    /// <summary>
    /// Blue and orange players in the latest frame, named or not. A subscriber that connected after players
    /// joined has frames for them but no roster, so its snapshot has empty teams.
    /// </summary>
    public int ActivePlayerCount { get; private set; }

    public void Apply(Envelope envelope)
    {
        lock (_lock)
        {
            switch (envelope.MessageCase)
            {
                case Envelope.MessageOneofCase.Header:
                    ApplyHeader(envelope.Header);
                    break;
                case Envelope.MessageOneofCase.Frame:
                    ApplyFrame(envelope.Frame);
                    break;
                case Envelope.MessageOneofCase.Footer:
                    Ended = true;
                    break;
            }
        }
    }

    private void ApplyHeader(CaptureHeader header)
    {
        var arena = header.EchoArena;
        if (arena is null) return;

        // A reconnect replays the same header, whose roster is older than the one built from join events
        if (arena.SessionId != _sessionId)
            _roster.Clear();

        HasData = true;
        _sessionId = arena.SessionId;
        _mapName = arena.MapName;
        _totalRoundCount = arena.TotalRoundCount;
        if (arena.TeamNames.Count > 0 && !string.IsNullOrWhiteSpace(arena.TeamNames[0])) _blueTeamName = arena.TeamNames[0];
        if (arena.TeamNames.Count > 1 && !string.IsNullOrWhiteSpace(arena.TeamNames[1])) _orangeTeamName = arena.TeamNames[1];

        foreach (var p in arena.InitialRoster)
        {
            if (!_roster.ContainsKey(p.Slot) && !_departed.ContainsKey(p.AccountNumber))
                _roster[p.Slot] = NewRosterEntry(p.AccountNumber, p.DisplayName, p.Role, p.JerseyNumber, p.Level);
        }
    }

    private void ApplyFrame(Frame frame)
    {
        var arena = frame.EchoArena;
        if (arena is null) return;

        HasData = true;

        // The frame a late subscriber receives first only carries replayed events.
        if (arena.GameStatus != GameStatus.Unspecified)
        {
            _gameStatus = arena.GameStatus;
            _gameClock = arena.GameClock;
            _bluePoints = arena.BluePoints;
            _orangePoints = arena.OrangePoints;
            _pauseState = arena.PauseState;
            _pauseDetail = arena.PauseDetail;
            ActivePlayerCount = arena.Players.Count(p => ((p.Flags >> 5) & 0b11) <= 1);

            foreach (var ps in arena.Players)
                _playerStates[ps.Slot] = ps;
        }

        // GoalScored is the engine's last_score block re-sent on later frames, not a goal event.
        // PlayerGoal marks the goal, and only a GoalScored in that same frame describes it.
        var goalScored = arena.Events
            .Where(e => e.EventCase == EchoEvent.EventOneofCase.GoalScored && IsRealGoal(e.GoalScored))
            .Select(e => e.GoalScored)
            .LastOrDefault();

        foreach (var evt in arena.Events)
            ApplyEvent(evt, goalScored);
    }

    private void ApplyEvent(EchoEvent evt, GoalScored? goalScored)
    {
        switch (evt.EventCase)
        {
            case EchoEvent.EventOneofCase.ScoreboardUpdated:
            {
                var s = evt.ScoreboardUpdated;
                _bluePoints = s.BluePoints;
                _orangePoints = s.OrangePoints;
                _blueRoundScore = s.BlueRoundScore;
                _orangeRoundScore = s.OrangeRoundScore;
                if (!string.IsNullOrEmpty(s.GameClockDisplay)) _gameClockDisplay = s.GameClockDisplay;
                break;
            }
            case EchoEvent.EventOneofCase.PlayerStatsUpdated:
                _stats[evt.PlayerStatsUpdated.PlayerSlot] = evt.PlayerStatsUpdated;
                break;
            case EchoEvent.EventOneofCase.PlayerJoined:
            {
                var j = evt.PlayerJoined;
                _roster[j.Slot] = NewRosterEntry(j.AccountNumber, j.DisplayName, j.Role, j.JerseyNumber, j.Level);
                _stats.Remove(j.Slot);
                _playerStates.Remove(j.Slot);
                _departed.Remove(j.AccountNumber);
                break;
            }
            case EchoEvent.EventOneofCase.PlayerLeft:
            {
                var slot = evt.PlayerLeft.PlayerSlot;
                if (_roster.Remove(slot, out var left))
                {
                    _departed[left.AccountNumber] = new DepartedPlayer
                    {
                        Slot = slot,
                        Entry = left,
                        Stats = _stats.GetValueOrDefault(slot),
                        State = _playerStates.GetValueOrDefault(slot)
                    };
                }
                _stats.Remove(slot);
                _playerStates.Remove(slot);
                break;
            }
            case EchoEvent.EventOneofCase.PlayerSwitchedTeam:
                if (_roster.TryGetValue(evt.PlayerSwitchedTeam.PlayerSlot, out var switched))
                    switched.Role = evt.PlayerSwitchedTeam.NewRole;
                break;
            case EchoEvent.EventOneofCase.PlayerInfoUpdated:
                if (_roster.TryGetValue(evt.PlayerInfoUpdated.PlayerSlot, out var updated))
                {
                    updated.JerseyNumber = evt.PlayerInfoUpdated.JerseyNumber;
                    updated.Level = evt.PlayerInfoUpdated.Level;
                }
                break;
            case EchoEvent.EventOneofCase.PlayerGoal:
                ApplyGoal(evt.PlayerGoal, goalScored);
                break;
            case EchoEvent.EventOneofCase.MatchEnded:
                Ended = true;
                break;
        }
    }

    private void ApplyGoal(PlayerGoal goal, GoalScored? scored)
    {
        if (!_roster.TryGetValue(goal.PlayerSlot, out var scorer)) return;

        if (scored is not null && (scored.PersonScored != scorer.DisplayName || scored.PointAmount != goal.Points))
            scored = null;

        // Without a matching GoalScored the type can only be inferred from the points.
        var goalType = scored?.GoalType ?? (goal.Points >= 3 ? GoalType.LongShot : GoalType.InsideShot);
        double? distance = scored?.DistanceThrown;
        // disc_speed has only ever been observed as garbage (2.1e37), so implausible values are dropped.
        double? speed = scored is { DiscSpeed: > 0 and < 100 } ? scored.DiscSpeed : null;

        _lastScore = new LastScoreInfo
        {
            DiscSpeed = speed,
            Team = RoleToTeam(scored?.Team ?? scorer.Role),
            GoalType = GoalTypeName(goalType),
            PointAmount = goal.Points,
            DistanceThrown = distance,
            PersonScored = scorer.DisplayName,
            AssistScored = scored?.AssistScored
        };

        if (!_shots.TryGetValue(scorer.AccountNumber, out var tally))
            _shots[scorer.AccountNumber] = tally = new ShotTally();

        if (speed is not null) tally.ShotSpeed.Add(speed);
        if (distance is not null) tally.ThrowDistance.Add(distance);
        switch (goalType)
        {
            case GoalType.InsideShot:
                tally.TwoPointShots++;
                break;
            case GoalType.LongShot:
                tally.ThreePointShots++;
                break;
            case GoalType.BounceShot:
                tally.TwoPointShots++;
                tally.ShortBounceShots++;
                break;
            case GoalType.LongBounceShot:
                tally.ThreePointShots++;
                tally.LongBounceShots++;
                break;
        }
    }

    /// <summary>
    /// Returns a snapshot with teams ordered [blue, orange, spectators], or null before any telemetry arrived.
    /// </summary>
    public EchoVrApiSession? ToSession()
    {
        lock (_lock)
        {
            if (!HasData) return null;

            var blue = new List<Player>();
            var orange = new List<Player>();
            var spectators = new List<Player>();

            void Place(Role team, Player player)
            {
                switch (team)
                {
                    case Role.BlueTeam: blue.Add(player); break;
                    case Role.OrangeTeam: orange.Add(player); break;
                    default: spectators.Add(player); break;
                }
            }

            foreach (var (slot, entry) in _roster.OrderBy(kv => kv.Key))
            {
                _playerStates.TryGetValue(slot, out var state);
                Place(TeamOf(entry, state), BuildPlayer(slot, entry, state, _stats.GetValueOrDefault(slot), left: false));
            }

            // After active players so they never take one of a team's four scoreboard rows.
            foreach (var d in _departed.Values.OrderBy(d => d.Slot))
                Place(TeamOf(d.Entry, d.State), BuildPlayer(d.Slot, d.Entry, d.State, d.Stats, left: true));

            return new EchoVrApiSession
            {
                SessionId = _sessionId,
                MapName = _mapName,
                GameStatus = Ended ? "post_match" : GameStatusName(_gameStatus),
                GameClock = _gameClock,
                GameClockDisplay = FormatGameClock(),
                BluePoints = _bluePoints,
                OrangePoints = _orangePoints,
                BlueRoundScore = _blueRoundScore,
                OrangeRoundScore = _orangeRoundScore,
                TotalRoundCount = _totalRoundCount,
                LastScore = _lastScore is null ? null : CloneLastScore(_lastScore),
                Pause = BuildPause(),
                Teams =
                [
                    new Team { TeamName = _blueTeamName, Players = blue },
                    new Team { TeamName = _orangeTeamName, Players = orange },
                    new Team { TeamName = "SPECTATORS", Players = spectators }
                ]
            };
        }
    }

    private Player BuildPlayer(int slot, RosterEntry entry, PlayerState? state, PlayerStatsUpdated? s, bool left)
    {
        _shots.TryGetValue(entry.AccountNumber, out var tally);

        var flags = state?.Flags ?? 0;
        return new Player
        {
            Name = entry.DisplayName,
            PlayerId = slot,
            UserId = (long)entry.AccountNumber,
            Number = entry.JerseyNumber,
            Level = entry.Level,
            Ping = state is null ? null : (int)state.Ping,
            PacketLossRatio = state?.PacketLossRatio,
            Stunned = (flags & 1) != 0,
            Invulnerable = (flags & 2) != 0,
            Blocking = (flags & 4) != 0,
            Possession = (flags & 8) != 0,
            IsEmotePlaying = (flags & 16) != 0,
            PlayerLeft = left,
            Stats = new PlayerStats
            {
                PossessionTime = state?.PossessionTime ?? 0,
                Points = s?.Points ?? 0,
                Saves = s?.Saves ?? 0,
                Goals = s?.Goals ?? 0,
                Stuns = s?.Stuns ?? 0,
                Passes = s?.Passes ?? 0,
                Catches = s?.Catches ?? 0,
                Steals = s?.Steals ?? 0,
                Blocks = s?.Blocks ?? 0,
                Interceptions = s?.Interceptions ?? 0,
                Assists = s?.Assists ?? 0,
                ShotsTaken = s?.ShotsTaken ?? 0,
                TwoPointShots = tally?.TwoPointShots ?? 0,
                ThreePointShots = tally?.ThreePointShots ?? 0,
                ShortBounceShots = tally?.ShortBounceShots ?? 0,
                LongBounceShots = tally?.LongBounceShots ?? 0,
                ShotSpeed = tally is null ? [] : [.. tally.ShotSpeed],
                ThrowDistance = tally is null ? [] : [.. tally.ThrowDistance]
            }
        };
    }

    // Falls back to the team index in PlayerState.Flags bits 5-6 when the roster has no team.
    private static Role TeamOf(RosterEntry entry, PlayerState? state)
    {
        if (entry.Role is Role.BlueTeam or Role.OrangeTeam or Role.Spectator || state is null)
            return entry.Role;

        return ((state.Flags >> 5) & 0b11) switch
        {
            0 => Role.BlueTeam,
            1 => Role.OrangeTeam,
            _ => Role.Spectator
        };
    }

    // ScoreboardUpdated's display string only changes on a goal; every frame carries the clock itself.
    private string FormatGameClock()
    {
        if (_gameStatus == GameStatus.Unspecified) return _gameClockDisplay;

        var clock = TimeSpan.FromSeconds(Math.Max(0, _gameClock));
        return $"{(int)clock.TotalMinutes:00}:{clock.Seconds:00}.{clock.Milliseconds / 10:00}";
    }

    private static bool IsRealGoal(GoalScored goal) =>
        goal.GoalType != GoalType.NoGoal &&
        !string.IsNullOrEmpty(goal.PersonScored) &&
        goal.PersonScored != "[INVALID]";

    private static RosterEntry NewRosterEntry(ulong accountNumber, string displayName, Role role, int jerseyNumber, int level) => new()
    {
        AccountNumber = accountNumber,
        DisplayName = displayName,
        Role = role,
        JerseyNumber = jerseyNumber,
        Level = level
    };

    private PauseInfo BuildPause() => new()
    {
        PausedState = PauseStateName(_pauseState),
        UnpausedTeam = RoleToTeam(_pauseDetail?.UnpausedTeam ?? Role.Unspecified),
        PausedRequestedTeam = RoleToTeam(_pauseDetail?.PausedRequestedTeam ?? Role.Unspecified),
        UnpausedTimer = _pauseDetail?.UnpausedTimer,
        PausedTimer = _pauseDetail?.PausedTimer
    };

    private static LastScoreInfo CloneLastScore(LastScoreInfo s) => new()
    {
        DiscSpeed = s.DiscSpeed,
        Team = s.Team,
        GoalType = s.GoalType,
        PointAmount = s.PointAmount,
        DistanceThrown = s.DistanceThrown,
        PersonScored = s.PersonScored,
        AssistScored = s.AssistScored
    };

    private static string GameStatusName(GameStatus status) => status switch
    {
        GameStatus.PreMatch => "pre_match",
        GameStatus.RoundStart => "round_start",
        GameStatus.Playing => "playing",
        GameStatus.Score => "score",
        GameStatus.RoundOver => "round_over",
        GameStatus.PostMatch => "post_match",
        GameStatus.PreSuddenDeath => "pre_sudden_death",
        GameStatus.SuddenDeath => "sudden_death",
        GameStatus.PostSuddenDeath => "post_sudden_death",
        _ => ""
    };

    private static string PauseStateName(PauseState state) => state switch
    {
        PauseState.NotPaused => "unpaused",
        PauseState.Paused => "paused",
        PauseState.Unpausing => "unpausing",
        PauseState.AutopauseReplay => "autopause_replay",
        PauseState.PausedRequested => "paused_requested",
        _ => "none"
    };

    private static string GoalTypeName(GoalType type) => type switch
    {
        GoalType.InsideShot => "INSIDE SHOT",
        GoalType.LongShot => "LONG SHOT",
        GoalType.BounceShot => "BOUNCE SHOT",
        GoalType.LongBounceShot => "LONG BOUNCE SHOT",
        GoalType.SelfGoal => "SELF GOAL",
        GoalType.SlamDunk => "SLAM DUNK",
        GoalType.Headbutt => "HEADBUTT",
        GoalType.LongHeadbutt => "LONG HEADBUTT",
        GoalType.BumperShot => "BUMPER SHOT",
        GoalType.LongBumperShot => "LONG BUMPER SHOT",
        _ => "[NO GOAL]"
    };

    private static string RoleToTeam(Role role) => role switch
    {
        Role.BlueTeam => "blue",
        Role.OrangeTeam => "orange",
        _ => "none"
    };
}
