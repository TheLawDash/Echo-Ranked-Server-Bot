namespace EchoRankedServerBot.Data.Entities;

public class PlayerMatchStat
{
    public int Id { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public long PlayerId { get; set; }
    public string? DiscordUsername { get; set; }
    public string? DiscordId { get; set; }
    public string? EvrId { get; set; }
    public string? UserId { get; set; }
    public string? UserIp { get; set; }
    public string MatchName { get; set; } = string.Empty;
    public DateTimeOffset Timestamp { get; set; }
    public bool Win { get; set; }
    public bool Lose { get; set; }
    public bool Mvp { get; set; }
    public double MvpScore { get; set; }

    // Stats
    public int Points { get; set; }
    public int Saves { get; set; }
    public int Assists { get; set; }
    public double PossessionTime { get; set; }
    public int Stuns { get; set; }
    public int Passes { get; set; }
    public int Catches { get; set; }
    public int Steals { get; set; }
    public int Blocks { get; set; }
    public int Interceptions { get; set; }
    public int Goals { get; set; }
    public int ShotsTaken { get; set; }
    public int LongBounceShots { get; set; }
    public int ThreePointShots { get; set; }
    public int TwoPointShots { get; set; }
    public int ShortBounceShots { get; set; }

    // Stored as JSON arrays
    public string? ThrowDistances { get; set; }
    public string? ShotSpeeds { get; set; }
}
