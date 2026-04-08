using System.Text.Json.Serialization;

namespace EchoRankedServerBot.Models.Latency;

public class GameServerLatencyModel
{
    [JsonPropertyName("game_server_latencies")]
    public Dictionary<string, List<LatencyEntry>> GameServerLatencies { get; set; } = new();
}
