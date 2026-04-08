using System.Text.Json.Serialization;

namespace EchoRankedServerBot.Models.EchoApi;

public class LastScoreInfo
{
    [JsonPropertyName("disc_speed")]
    public double? DiscSpeed { get; set; }

    [JsonPropertyName("team")]
    public string? Team { get; set; }

    [JsonPropertyName("goal_type")]
    public string? GoalType { get; set; }

    [JsonPropertyName("point_amount")]
    public int? PointAmount { get; set; }

    [JsonPropertyName("distance_thrown")]
    public double? DistanceThrown { get; set; }

    [JsonPropertyName("person_scored")]
    public string? PersonScored { get; set; }

    [JsonPropertyName("assist_scored")]
    public string? AssistScored { get; set; }
}
