using System.Text.Json.Serialization;

namespace EchoTelemetryCli.Models;

public class TokenRequest
{
    [JsonPropertyName("username")]
    public required string Username { get; set; }

    [JsonPropertyName("password")]
    public required string Password { get; set; }
}
