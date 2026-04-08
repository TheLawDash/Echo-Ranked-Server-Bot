using System.Text.Json.Serialization;

namespace EchoRankedServerBot.Models.Nakama;

public class NakamaMatches
{
    [JsonPropertyName("system_start_time")]
    public DateTime? SystemStartTime { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime? Timestamp { get; set; }

    [JsonPropertyName("labels")]
    public List<MatchLabel> Labels { get; set; } = [];
}
