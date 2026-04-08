using System.Text.Json.Serialization;

namespace EchoRankedServerBot.Models.Nakama;

public class NakamaStorageRequest
{
    [JsonPropertyName("objectIds")]
    public List<NakamaCollectionRequestData> CollectionData { get; set; } = [];
}

public class NakamaCollectionRequestData
{
    [JsonPropertyName("collection")]
    public string Collection { get; set; } = string.Empty;

    [JsonPropertyName("key")]
    public string Key { get; set; } = string.Empty;

    [JsonPropertyName("user_id")]
    public string UserId { get; set; } = string.Empty;
}
