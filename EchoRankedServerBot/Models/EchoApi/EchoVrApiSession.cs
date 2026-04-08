using System.Text.Json.Serialization;

namespace EchoRankedServerBot.Models.EchoApi;

public class EchoVrApiSession
{
    [JsonPropertyName("disc")]
    public DiscObject? Disc { get; set; }

    [JsonPropertyName("orange_team_restart_request")]
    public int? OrangeTeamRestartRequest { get; set; }

    [JsonPropertyName("sessionid")]
    public string? SessionId { get; set; }

    [JsonPropertyName("game_clock_display")]
    public string? GameClockDisplay { get; set; }

    [JsonPropertyName("game_status")]
    public string? GameStatus { get; set; }

    [JsonPropertyName("sessionip")]
    public string? SessionIp { get; set; }

    [JsonPropertyName("match_type")]
    public string? MatchType { get; set; }

    [JsonPropertyName("map_name")]
    public string? MapName { get; set; }

    [JsonPropertyName("right_shoulder_pressed2")]
    public double? RightShoulderPressed2 { get; set; }

    [JsonPropertyName("teams")]
    public List<Team>? Teams { get; set; }

    [JsonPropertyName("blue_round_score")]
    public int? BlueRoundScore { get; set; }

    [JsonPropertyName("orange_points")]
    public int? OrangePoints { get; set; }

    [JsonPropertyName("player")]
    public Player? Player { get; set; }

    [JsonPropertyName("private_match")]
    public bool? PrivateMatch { get; set; }

    [JsonPropertyName("blue_team_restart_request")]
    public int? BlueTeamRestartRequest { get; set; }

    [JsonPropertyName("tournament_match")]
    public bool? TournamentMatch { get; set; }

    [JsonPropertyName("orange_round_score")]
    public int? OrangeRoundScore { get; set; }

    [JsonPropertyName("rules_changed_by")]
    public string? RulesChangedBy { get; set; }

    [JsonPropertyName("total_round_count")]
    public int? TotalRoundCount { get; set; }

    [JsonPropertyName("left_shoulder_pressed2")]
    public double? LeftShoulderPressed2 { get; set; }

    [JsonPropertyName("left_shoulder_pressed")]
    public double? LeftShoulderPressed { get; set; }

    [JsonPropertyName("pause")]
    public PauseInfo? Pause { get; set; }

    [JsonPropertyName("right_shoulder_pressed")]
    public double? RightShoulderPressed { get; set; }

    [JsonPropertyName("blue_points")]
    public int? BluePoints { get; set; }

    [JsonPropertyName("last_throw")]
    public LastThrowInfo? LastThrow { get; set; }

    [JsonPropertyName("client_name")]
    public string? ClientName { get; set; }

    [JsonPropertyName("game_clock")]
    public double? GameClock { get; set; }

    [JsonPropertyName("possesion")]
    public List<int>? Possession { get; set; }

    [JsonPropertyName("last_score")]
    public LastScoreInfo? LastScore { get; set; }

    [JsonPropertyName("rules_changed_at")]
    public long? RulesChangedAt { get; set; }

    [JsonPropertyName("err_code")]
    public int? ErrCode { get; set; }
}
