using System.Text.Json.Serialization;

namespace EchoRankedServerBot.Models.Nakama;

public class Broadcaster
{
    [JsonPropertyName("sid")]
    public string Sid { get; set; } = string.Empty;

    [JsonPropertyName("oper")]
    public string Oper { get; set; } = string.Empty;

    [JsonPropertyName("group_ids")]
    public List<string> GroupIds { get; set; } = [];

    [JsonPropertyName("endpoint")]
    public string Endpoint { get; set; } = string.Empty;

    [JsonPropertyName("version_lock")]
    public string VersionLock { get; set; } = string.Empty;

    [JsonPropertyName("region_codes")]
    public List<string> RegionCodes { get; set; } = [];

    [JsonPropertyName("server_id")]
    public object? ServerId { get; set; }

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = [];
}
