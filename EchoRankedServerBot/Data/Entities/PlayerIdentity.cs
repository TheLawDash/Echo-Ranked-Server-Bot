namespace EchoRankedServerBot.Data.Entities;

public class PlayerIdentity
{
    public int Id { get; set; }
    public string DiscordId { get; set; } = string.Empty;
    public string NakamaId { get; set; } = string.Empty;
    public string? EvrId { get; set; }
    public string? PlayerName { get; set; }
    public DateTimeOffset FirstSeenAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset LastSeenAt { get; set; } = DateTimeOffset.UtcNow;
}
