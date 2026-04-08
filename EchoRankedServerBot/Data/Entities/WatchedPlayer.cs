namespace EchoRankedServerBot.Data.Entities;

public class WatchedPlayer
{
    public int Id { get; set; }
    public string DiscordId { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
