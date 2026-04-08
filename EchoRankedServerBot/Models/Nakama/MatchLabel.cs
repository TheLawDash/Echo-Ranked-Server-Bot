using System.Text.Json.Serialization;

namespace EchoRankedServerBot.Models.Nakama;

public class MatchLabel
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("open")]
    public bool? Open { get; set; }

    [JsonPropertyName("lobby_type")]
    public string LobbyType { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public int? Size { get; set; }

    [JsonPropertyName("player_count")]
    public int? PlayerCount { get; set; }

    [JsonPropertyName("start_time")]
    public object? StartTime { get; set; }

    [JsonPropertyName("created_at")]
    public string? CreatedAt { get; set; }

    [JsonPropertyName("broadcaster")]
    public Broadcaster Broadcaster { get; set; } = new();

    [JsonPropertyName("players")]
    public List<NakamaPlayer>? Players { get; set; }
}
