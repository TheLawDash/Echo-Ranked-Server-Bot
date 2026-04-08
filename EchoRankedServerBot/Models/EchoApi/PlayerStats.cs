using System.Text.Json.Serialization;

namespace EchoRankedServerBot.Models.EchoApi;

public class PlayerStats
{
    [JsonPropertyName("possession_time")]
    public double PossessionTime { get; set; }

    [JsonPropertyName("points")]
    public int Points { get; set; }

    [JsonPropertyName("saves")]
    public int Saves { get; set; }

    [JsonPropertyName("goals")]
    public int Goals { get; set; }

    [JsonPropertyName("stuns")]
    public int Stuns { get; set; }

    [JsonPropertyName("passes")]
    public int Passes { get; set; }

    [JsonPropertyName("catches")]
    public int Catches { get; set; }

    [JsonPropertyName("steals")]
    public int Steals { get; set; }

    [JsonPropertyName("blocks")]
    public int Blocks { get; set; }

    [JsonPropertyName("interceptions")]
    public int Interceptions { get; set; }

    [JsonPropertyName("assists")]
    public int Assists { get; set; }

    [JsonPropertyName("shots_taken")]
    public int ShotsTaken { get; set; }

    public int LongBounceShots { get; set; }
    public int ShortBounceShots { get; set; }
    public int ThreePointShots { get; set; }
    public int TwoPointShots { get; set; }
    public List<double?> ShotSpeed { get; set; } = [];
    public List<double?> ThrowDistance { get; set; } = [];
}
