using System.Text.Json.Serialization;

namespace EchoRankedServerBot.Models.Nakama;

public class NakamaPlayer
{
    [JsonPropertyName("display_name")]
    public string DisplayName { get; set; } = string.Empty;

    [JsonPropertyName("party_id")]
    public string PartyId { get; set; } = string.Empty;

    [JsonPropertyName("team")]
    public string Team { get; set; } = string.Empty;

    [JsonPropertyName("rating_mu")]
    public double? RatingMu { get; set; }

    [JsonPropertyName("rating_sigma")]
    public double? RatingSigma { get; set; }

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("discord_id")]
    public string DiscordId { get; set; } = string.Empty;

    [JsonPropertyName("user_id")]
    public string UserId { get; set; } = string.Empty;

    [JsonPropertyName("evr_id")]
    public string EvrId { get; set; } = string.Empty;

    [JsonPropertyName("client_ip")]
    public string ClientIp { get; set; } = string.Empty;
}
