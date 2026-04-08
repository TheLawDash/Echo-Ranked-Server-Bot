using System.Text.Json.Serialization;

namespace EchoRankedServerBot.Models.EchoApi;

public class Player
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("playerid")]
    public int? PlayerId { get; set; }

    [JsonPropertyName("userid")]
    public long? UserId { get; set; }

    [JsonPropertyName("is_emote_playing")]
    public bool? IsEmotePlaying { get; set; }

    [JsonPropertyName("number")]
    public int? Number { get; set; }

    [JsonPropertyName("level")]
    public int? Level { get; set; }

    [JsonPropertyName("stunned")]
    public bool? Stunned { get; set; }

    [JsonPropertyName("ping")]
    public int? Ping { get; set; }

    [JsonPropertyName("packetlossratio")]
    public double? PacketLossRatio { get; set; }

    [JsonPropertyName("invulnerable")]
    public bool? Invulnerable { get; set; }

    [JsonPropertyName("holding_left")]
    public string? HoldingLeft { get; set; }

    [JsonPropertyName("possession")]
    public bool? Possession { get; set; }

    [JsonPropertyName("blocking")]
    public bool? Blocking { get; set; }

    [JsonPropertyName("velocity")]
    public List<double>? Velocity { get; set; }

    [JsonPropertyName("stats")]
    public PlayerStats? Stats { get; set; }

    [JsonPropertyName("holding_right")]
    public string? HoldingRight { get; set; }

    [JsonPropertyName("playerLeft")]
    public bool? PlayerLeft { get; set; }
}
