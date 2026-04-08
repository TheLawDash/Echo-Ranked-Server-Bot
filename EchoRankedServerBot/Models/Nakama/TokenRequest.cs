using System.Text.Json.Serialization;

namespace EchoRankedServerBot.Models.Nakama;

public class TokenRequest
{
    [JsonPropertyName("username")]
    public required string Username { get; set; }

    [JsonPropertyName("password")]
    public required string Password { get; set; }
}
