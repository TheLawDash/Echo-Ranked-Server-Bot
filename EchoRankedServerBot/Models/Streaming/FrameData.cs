using System.Text.Json.Serialization;

namespace EchoRankedServerBot.Models.Streaming;

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
