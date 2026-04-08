using EchoRankedServerBot.Models.EchoApi;

namespace EchoRankedServerBot.Models.Match;

public class DiscordPlayerDetails
{
    public Player? Player { get; set; }
    public List<Player>? BeforeLeave { get; set; }
    public string? Username { get; set; }
    public ulong MemberId { get; set; }
    public string? UserId { get; set; }
    public string? EvrId { get; set; }
    public string? UserIp { get; set; }
    public string? UserTeam { get; set; }
    public bool HasLeft { get; set; }
    public int LeaveTimes { get; set; }
    public bool DetectedBool { get; set; }
    public bool NotInQueue { get; set; }
    public string? AssignedTeam { get; set; }
    public string? DiscordId { get; set; }
}
