using System.Text.Json.Serialization;

namespace EchoRankedServerBot.Models.Streaming;

public class LobbySessionEventsResponse
{
    [JsonPropertyName("lobby_session_id")]
    public string? LobbySessionId { get; set; }

    [JsonPropertyName("events")]
    public List<EventFrame>? Events { get; set; }
}
