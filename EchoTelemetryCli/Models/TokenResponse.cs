using System.Text.Json.Serialization;

namespace EchoTelemetryCli.Models;

public class TokenResponse
{
    [JsonPropertyName("token")]
    public required string Token { get; set; }

    [JsonPropertyName("refresh_token")]
    public required string RefreshToken { get; set; }
}
