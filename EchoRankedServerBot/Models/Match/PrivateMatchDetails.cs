namespace EchoRankedServerBot.Models.Match;

public class PrivateMatchDetails
{
    public string? QueueNumber { get; set; }
    public ulong QueueChannelId { get; set; }
    public bool MatchStarting { get; set; }
    public ulong? LiveMatchMessageId { get; set; }
    public bool StatsUploaded { get; set; }
    public ulong? MatchMessageId { get; set; }
    public ulong? SparkLinkMessageId { get; set; }
    public ulong? NeatQueueMessageId { get; set; }
    public string? DecidedRegion { get; set; }
    public double? DecidedAverageLatency { get; set; }
    public int? PlayersUsedForDecision { get; set; }
}
