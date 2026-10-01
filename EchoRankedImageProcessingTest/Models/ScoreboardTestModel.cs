using EchoRankedServerBot.Models.EchoApi;
using EchoRankedServerBot.Models.Match;

namespace EchoRankedImageProcessingTest.Models;

public class ScoreboardTestModel
{
    // Path to template image
    public required string TemplatePath { get; set; }
    public required EchoVrApiSession EchoMatchData { get; set; }
    public required List<PlayerScore> PlayerScores { get; set; }
    public string MatchId { get; set; } = "Test-Image-Match";
}