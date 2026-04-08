using System.Text.Json.Serialization;
using EchoRankedServerBot.Models.EchoApi;

namespace EchoRankedServerBot.Models.Streaming;

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

    [JsonPropertyName("disc")]
    public DiscObject? Disc { get; set; }

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

    [JsonPropertyName("possession")]
    public List<int>? Possession { get; set; }

    [JsonPropertyName("game_clock")]
    public double? GameClock { get; set; }

    [JsonPropertyName("last_score")]
    public LastScoreInfo? LastScore { get; set; }

    [JsonPropertyName("last_throw")]
    public LastThrowInfo? LastThrow { get; set; }

    [JsonPropertyName("pause")]
    public PauseInfo? Pause { get; set; }

    [JsonPropertyName("private_match")]
    public bool? PrivateMatch { get; set; }

    [JsonPropertyName("tournament_match")]
    public bool? TournamentMatch { get; set; }
}
