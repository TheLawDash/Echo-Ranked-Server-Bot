using System.Text.Json.Serialization;

namespace EchoRankedServerBot.Models.Nakama;

public class PrepareEchoMatchRequest
{
    [JsonPropertyName("id")]
    public required string Id { get; set; }

    [JsonPropertyName("mode")]
    public required string Mode { get; set; }

    [JsonPropertyName("spawned_by")]
    public required string SpawnedBy { get; set; }

    [JsonPropertyName("role_alignments")]
    public Dictionary<string, int> RoleAlignments { get; set; } = new();

    [JsonPropertyName("guild_id")]
    public required string GuildId { get; set; }
}
