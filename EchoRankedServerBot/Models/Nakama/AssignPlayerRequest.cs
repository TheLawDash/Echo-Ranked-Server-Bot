using System.Text.Json.Serialization;

namespace EchoRankedServerBot.Models.Nakama;

public class AssignPlayerRequest
{
    [JsonPropertyName("user_id")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("match_id")]
    public string MatchId { get; set; } = string.Empty;

    [JsonPropertyName("role")]
    public string Role { get; set; } = string.Empty;
}
