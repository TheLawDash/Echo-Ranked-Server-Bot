using System.Text.Json.Serialization;

namespace EchoRankedServerBot.Models.Streaming;

public class EventFrame
{
    [JsonPropertyName("frame")]
    public FrameData? Frame { get; set; }
}
