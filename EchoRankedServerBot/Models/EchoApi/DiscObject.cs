using System.Text.Json.Serialization;

namespace EchoRankedServerBot.Models.EchoApi;

public class DiscObject
{
    [JsonPropertyName("position")]
    public List<double>? Position { get; set; }

    [JsonPropertyName("forward")]
    public List<double>? Forward { get; set; }

    [JsonPropertyName("left")]
    public List<double>? Left { get; set; }

    [JsonPropertyName("up")]
    public List<double>? Up { get; set; }

    [JsonPropertyName("velocity")]
    public List<double>? Velocity { get; set; }

    [JsonPropertyName("bounce_count")]
    public int? BounceCount { get; set; }
}
