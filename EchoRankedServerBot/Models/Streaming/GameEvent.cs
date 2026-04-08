using System.Text.Json.Serialization;

namespace EchoRankedServerBot.Models.Streaming;

public class GameEvent
{
    [JsonPropertyName("matchEnded")]
    public object? MatchEnded { get; set; }

    [JsonPropertyName("roundEnded")]
    public object? RoundEnded { get; set; }

    [JsonPropertyName("goalScored")]
    public object? GoalScored { get; set; }

    public bool IsMatchEnded => MatchEnded != null;
    public bool IsRoundEnded => RoundEnded != null;
    public bool IsGoalScored => GoalScored != null;
}
