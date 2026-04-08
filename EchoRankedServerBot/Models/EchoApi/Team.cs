using System.Text.Json.Serialization;

namespace EchoRankedServerBot.Models.EchoApi;

public class Team
{
    [JsonPropertyName("players")]
    public List<Player>? Players { get; set; }

    [JsonPropertyName("team")]
    public string? TeamName { get; set; }

    [JsonPropertyName("posession")]
    public bool? Possession { get; set; }

    [JsonPropertyName("stats")]
    public TeamStats? Stats { get; set; }
}

public class TeamStats
{
    [JsonPropertyName("points")]
    public int? Points { get; set; }

    [JsonPropertyName("possession_time")]
    public double? PossessionTime { get; set; }

    [JsonPropertyName("interceptions")]
    public int? Interceptions { get; set; }

    [JsonPropertyName("blocks")]
    public int? Blocks { get; set; }

    [JsonPropertyName("steals")]
    public int? Steals { get; set; }

    [JsonPropertyName("catches")]
    public int? Catches { get; set; }

    [JsonPropertyName("passes")]
    public int? Passes { get; set; }

    [JsonPropertyName("saves")]
    public int? Saves { get; set; }

    [JsonPropertyName("goals")]
    public int? Goals { get; set; }

    [JsonPropertyName("stuns")]
    public int? Stuns { get; set; }

    [JsonPropertyName("assists")]
    public int? Assists { get; set; }

    [JsonPropertyName("shots_taken")]
    public int? ShotsTaken { get; set; }
}
