namespace EchoRankedServerBot.Configuration;

public class BotOptions
{
    public const string SectionName = "Bot";

    public ulong GuildId { get; set; }
    public ulong LiveMatchesChannelId { get; set; }
    public ulong ErrorChannelId { get; set; }
    public ulong LogChannelId { get; set; }
    public ulong AltChannelId { get; set; }
    public ulong EuRoleId { get; set; }
    public ulong NeatQueueBotId { get; set; }
    public ulong BotUserId { get; set; }
    public ulong OwnerUserId { get; set; }
    public ulong AdminRoleId { get; set; }
    public ulong NeatQueueChannelId { get; set; }
    public string PrimaryGuildId { get; set; } = string.Empty;
    public string BackupGuildId { get; set; } = string.Empty;
    public string SpawnedBy { get; set; } = string.Empty;
    public string NebraskaServerIp { get; set; } = string.Empty;
    public string PennsylvaniaServerIp { get; set; } = string.Empty;
    public string KansasServerIp { get; set; } = string.Empty;
    public string FallbackServerIp { get; set; } = string.Empty;
    public string ChicagoRegionCode { get; set; } = string.Empty;
    public string DallasRegionCode { get; set; } = string.Empty;
    public string RankedCompRegionCode { get; set; } = string.Empty;
    public string OmahaRegionCode { get; set; } = string.Empty;
    public string PgvDcCevrRegionCode { get; set; } = string.Empty;
}
