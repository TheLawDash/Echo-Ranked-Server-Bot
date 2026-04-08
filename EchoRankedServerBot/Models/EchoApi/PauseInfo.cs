using System.Text.Json.Serialization;

namespace EchoRankedServerBot.Models.EchoApi;

public class PauseInfo
{
    [JsonPropertyName("paused_state")]
    public string? PausedState { get; set; }

    [JsonPropertyName("unpaused_team")]
    public string? UnpausedTeam { get; set; }

    [JsonPropertyName("paused_requested_team")]
    public string? PausedRequestedTeam { get; set; }

    [JsonPropertyName("unpaused_timer")]
    public double? UnpausedTimer { get; set; }

    [JsonPropertyName("paused_timer")]
    public double? PausedTimer { get; set; }
}
