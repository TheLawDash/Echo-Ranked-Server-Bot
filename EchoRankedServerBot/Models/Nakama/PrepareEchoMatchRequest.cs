using System.Text.Json.Serialization;

namespace EchoRankedServerBot.Models.Nakama;

public class PrepareEchoMatchRequest
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("guild_id")]
    public required string GuildId { get; set; }

    [JsonPropertyName("owner_id")]
    public required string OwnerId { get; set; }

    [JsonPropertyName("mode")]
    public required string Mode { get; set; }

    [JsonPropertyName("level")]
    public string? Level { get; set; }

    [JsonPropertyName("region")]
    public string? Region { get; set; }

    [JsonPropertyName("team_alignments")]
    public Dictionary<string, string> TeamAlignments { get; set; } = new();
}
