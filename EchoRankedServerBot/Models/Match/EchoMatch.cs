namespace EchoRankedServerBot.Models.Match;

public class EchoMatch
{
    public required string MatchId { get; set; }
    public EchoMatchInstance? EchoMatchInstance { get; set; }
    public PrivateMatchDetails? PrivateMatchDetails { get; set; }
    public CancellationTokenSource? MonitoringCts { get; set; }
    public readonly object Lock = new();
}
