using System.Text.Json.Serialization;

namespace EchoRankedServerBot.Models.Latency;

public class LatencyEntry
{
    [JsonPropertyName("rtt")]
    public long LatencyInNanoSeconds { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonIgnore]
    public double LatencyInMs => LatencyInNanoSeconds / 1_000_000.0;
}
