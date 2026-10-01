using EchoRankedServerBot.Models.EchoApi;
using EchoRankedServerBot.Models.Match;

namespace EchoRankedImageProcessingTest.Utilities;

public static class PlayerExtensions
{
    private static readonly List<string> PlayerNames = ["Law1", "Law2", "LawLongName3", "Law_Long_Symbols_4"];

    private static Player PopulateMockPlayer(this Player player)
    {
        player.SetApplicableValuesWithinObject();
        var playerStats = new PlayerStats();
        playerStats.SetApplicableValuesWithinObject();
        player.Stats = playerStats;
        return player;
    }

    public static List<Player> GenerateMockPlayers(string teamColor)
    {
        // different positions each time, helps cover all variations of the images
        return PlayerNames.Shuffle().Select(x => new Player
        {
            Name = teamColor + x,
        }.PopulateMockPlayer()).ToList();
    }

    public static List<PlayerScore> GenerateMockPlayerScores(this List<Player> players)
    {
        var playerScores = players.Select(x => new PlayerScore
        {
            Player = x
        }).ToList();
        playerScores.SetApplicableValuesWithinObject();
        return playerScores;
    }
}