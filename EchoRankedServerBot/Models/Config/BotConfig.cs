using System.Text.Json.Serialization;

namespace EchoRankedServerBot.Models.Config;

public class BotConfig
{
    [JsonPropertyName("enforce_1000_mmr_party_restriction")]
    public bool Enforce1000MmrPartyRestriction { get; set; }
}
