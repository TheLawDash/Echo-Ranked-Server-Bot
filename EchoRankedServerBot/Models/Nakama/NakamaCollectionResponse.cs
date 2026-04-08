using System.Text.Json.Serialization;

namespace EchoRankedServerBot.Models.Nakama;

public class NakamaCollectionResponse
{
    [JsonPropertyName("objects")]
    public List<NakamaCollectionObject> Objects { get; set; } = [];
}

public class NakamaCollectionObject
{
    public string Collection { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("user_id")]
    public string UserId { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
}
