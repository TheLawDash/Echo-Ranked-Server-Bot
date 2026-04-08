using EchoRankedServerBot.Models.EchoApi;

namespace EchoRankedServerBot.Models.Match;

public class EchoMatchInstance
{
    public string BroadcasterId { get; set; } = string.Empty;
    public string? SessionId { get; set; }
    public bool PostingStats { get; set; }
    public bool StatsRecorded { get; set; }
    public LastScoreInfo? LastScore { get; set; }
    public bool LastScoreRecorded { get; set; }
    public DateTime StartedTime { get; set; }
    public Player? Mvp { get; set; }
    public List<DiscordPlayerDetails> PlayerDetails { get; set; } = [];
    public List<PlayerScore> PlayerScores { get; set; } = [];
}
