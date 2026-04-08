namespace EchoRankedServerBot.Models.Stats;

public class PostStatsRequest
{
    public string PlayerName { get; set; } = string.Empty;
    public long PlayerId { get; set; }
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
    public int ShortBounceShots { get; set; }
    public int ThreePointShots { get; set; }
    public int TwoPointShots { get; set; }
    public string ShotSpeed { get; set; } = string.Empty;
    public string ThrowDistance { get; set; } = string.Empty;
    public string PrivateName { get; set; } = string.Empty;
    public long TimeStamp { get; set; }
    public string? UserIp { get; set; }
    public string? EvrId { get; set; }
    public string? UserId { get; set; }
    public bool Win { get; set; }
    public bool Lose { get; set; }
    public bool Mvp { get; set; }
    public double MvpScore { get; set; }
    public string? DiscordUsername { get; set; }
    public string? DiscordId { get; set; }
}
