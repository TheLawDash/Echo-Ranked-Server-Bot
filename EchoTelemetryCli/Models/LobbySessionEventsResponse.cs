using System.Text.Json.Serialization;

namespace EchoTelemetryCli.Models;

public class LobbySessionEventsResponse
{
    [JsonPropertyName("lobby_session_id")]
    public string? LobbySessionId { get; set; }

    [JsonPropertyName("events")]
    public List<EventFrame>? Events { get; set; }
}

public class EventFrame
{
    [JsonPropertyName("frame")]
    public FrameData? Frame { get; set; }
}

public class FrameData
{
    [JsonPropertyName("frameIndex")]
    public long? FrameIndex { get; set; }

    [JsonPropertyName("timestamp")]
    public string? Timestamp { get; set; }

    [JsonPropertyName("events")]
    public List<GameEvent>? Events { get; set; }

    [JsonPropertyName("session")]
    public SessionData? Session { get; set; }
}

public class GameEvent
{
    [JsonPropertyName("matchEnded")]
    public object? MatchEnded { get; set; }

    [JsonPropertyName("roundEnded")]
    public object? RoundEnded { get; set; }

    [JsonPropertyName("goalScored")]
    public object? GoalScored { get; set; }

    public bool IsMatchEnded => MatchEnded != null;
    public bool IsRoundEnded => RoundEnded != null;
    public bool IsGoalScored => GoalScored != null;
}

public class SessionData
{
    [JsonPropertyName("sessionid")]
    public string? SessionId { get; set; }

    [JsonPropertyName("game_clock_display")]
    public string? GameClockDisplay { get; set; }

    [JsonPropertyName("game_status")]
    public string? GameStatus { get; set; }

    [JsonPropertyName("match_type")]
    public string? MatchType { get; set; }

    [JsonPropertyName("map_name")]
    public string? MapName { get; set; }

    [JsonPropertyName("orange_points")]
    public int? OrangePoints { get; set; }

    [JsonPropertyName("blue_points")]
    public int? BluePoints { get; set; }

    [JsonPropertyName("orange_round_score")]
    public int? OrangeRoundScore { get; set; }

    [JsonPropertyName("blue_round_score")]
    public int? BlueRoundScore { get; set; }

    [JsonPropertyName("total_round_count")]
    public int? TotalRoundCount { get; set; }

    [JsonPropertyName("teams")]
    public List<Team>? Teams { get; set; }

    [JsonPropertyName("game_clock")]
    public double? GameClock { get; set; }

    [JsonPropertyName("last_score")]
    public LastScoreInfo? LastScore { get; set; }

    [JsonPropertyName("pause")]
    public PauseInfo? Pause { get; set; }

    [JsonPropertyName("private_match")]
    public bool? PrivateMatch { get; set; }

    [JsonPropertyName("tournament_match")]
    public bool? TournamentMatch { get; set; }
}

public class Team
{
    [JsonPropertyName("players")]
    public List<Player>? Players { get; set; }

    [JsonPropertyName("team")]
    public string? TeamName { get; set; }

    [JsonPropertyName("stats")]
    public TeamStats? Stats { get; set; }
}

public class TeamStats
{
    [JsonPropertyName("points")]
    public int? Points { get; set; }

    [JsonPropertyName("possession_time")]
    public double? PossessionTime { get; set; }

    [JsonPropertyName("goals")]
    public int? Goals { get; set; }

    [JsonPropertyName("stuns")]
    public int? Stuns { get; set; }

    [JsonPropertyName("saves")]
    public int? Saves { get; set; }

    [JsonPropertyName("assists")]
    public int? Assists { get; set; }

    [JsonPropertyName("steals")]
    public int? Steals { get; set; }

    [JsonPropertyName("blocks")]
    public int? Blocks { get; set; }

    [JsonPropertyName("interceptions")]
    public int? Interceptions { get; set; }

    [JsonPropertyName("passes")]
    public int? Passes { get; set; }

    [JsonPropertyName("catches")]
    public int? Catches { get; set; }

    [JsonPropertyName("shots_taken")]
    public int? ShotsTaken { get; set; }
}

public class Player
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("playerid")]
    public int? PlayerId { get; set; }

    [JsonPropertyName("userid")]
    public long? UserId { get; set; }

    [JsonPropertyName("number")]
    public int? Number { get; set; }

    [JsonPropertyName("ping")]
    public int? Ping { get; set; }

    [JsonPropertyName("stunned")]
    public bool? Stunned { get; set; }

    [JsonPropertyName("possession")]
    public bool? Possession { get; set; }

    [JsonPropertyName("stats")]
    public PlayerStats? Stats { get; set; }

    [JsonPropertyName("playerLeft")]
    public bool? PlayerLeft { get; set; }
}

public class PlayerStats
{
    [JsonPropertyName("possession_time")]
    public double PossessionTime { get; set; }

    [JsonPropertyName("points")]
    public int Points { get; set; }

    [JsonPropertyName("saves")]
    public int Saves { get; set; }

    [JsonPropertyName("goals")]
    public int Goals { get; set; }

    [JsonPropertyName("stuns")]
    public int Stuns { get; set; }

    [JsonPropertyName("passes")]
    public int Passes { get; set; }

    [JsonPropertyName("catches")]
    public int Catches { get; set; }

    [JsonPropertyName("steals")]
    public int Steals { get; set; }

    [JsonPropertyName("blocks")]
    public int Blocks { get; set; }

    [JsonPropertyName("interceptions")]
    public int Interceptions { get; set; }

    [JsonPropertyName("assists")]
    public int Assists { get; set; }

    [JsonPropertyName("shots_taken")]
    public int ShotsTaken { get; set; }
}

public class LastScoreInfo
{
    [JsonPropertyName("disc_speed")]
    public double? DiscSpeed { get; set; }

    [JsonPropertyName("team")]
    public string? Team { get; set; }

    [JsonPropertyName("goal_type")]
    public string? GoalType { get; set; }

    [JsonPropertyName("point_amount")]
    public int? PointAmount { get; set; }

    [JsonPropertyName("distance_thrown")]
    public double? DistanceThrown { get; set; }

    [JsonPropertyName("person_scored")]
    public string? PersonScored { get; set; }

    [JsonPropertyName("assist_scored")]
    public string? AssistScored { get; set; }
}

public class PauseInfo
{
    [JsonPropertyName("paused_state")]
    public string? PausedState { get; set; }

    [JsonPropertyName("unpaused_team")]
    public string? UnpausedTeam { get; set; }

    [JsonPropertyName("paused_requested_team")]
    public string? PausedRequestedTeam { get; set; }

    [JsonPropertyName("unpaused_timer")]
    public double? UnpausedTimer { get; set; }

    [JsonPropertyName("paused_timer")]
    public double? PausedTimer { get; set; }
}
