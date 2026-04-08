using System.Text.Json;
using System.Text.Json.Serialization;

namespace EchoRankedServerBot.Models.Ip;

public class VpnApiResponse
{
    public string Status { get; set; } = string.Empty;

    [JsonExtensionData]
    public Dictionary<string, JsonElement> IpDetails { get; set; } = new();
}

public class VpnRoot
{
    [JsonPropertyName("proxy")]
    public string? Proxy { get; set; }

    [JsonPropertyName("type")]
    public string? Type { get; set; }

    [JsonPropertyName("operator")]
    public VpnOperator? Operator { get; set; }
}

public class VpnOperator
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonPropertyName("anonymity")]
    public string? Anonymity { get; set; }

    [JsonPropertyName("popularity")]
    public string? Popularity { get; set; }

    [JsonPropertyName("protocols")]
    public List<string>? Protocols { get; set; }
}
