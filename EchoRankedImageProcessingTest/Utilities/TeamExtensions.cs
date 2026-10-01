using EchoRankedServerBot.Models.EchoApi;

namespace EchoRankedImageProcessingTest.Utilities;

public static class TeamExtensions
{
    public static Team PopulateMockTeam(this Team team)
    {
        // Team names not nullable, callers will call it populated
        var players = PlayerExtensions.GenerateMockPlayers(team.TeamName!.ToLower());
        var teamStats = PopulateMockTeamStats(players);
        team.Players = players;
        team.Stats = teamStats;
        team.SetApplicableValuesWithinObject();
        return team;
    }

    private static TeamStats PopulateMockTeamStats(List<Player> players)
    {
        // Again, not null, populated by the subsequent call
        var teamStats = new TeamStats
        {
            Points = players.Sum(x => x.Stats!.Points),
            Interceptions = players.Sum(x => x.Stats!.Interceptions),
            Blocks = players.Sum(x => x.Stats!.Blocks),
            Steals = players.Sum(x => x.Stats!.Steals),
            Passes = players.Sum(x => x.Stats!.Passes),
            Saves = players.Sum(x => x.Stats!.Saves),
            Goals = players.Sum(x => x.Stats!.Goals),
            Stuns = players.Sum(x => x.Stats!.Stuns),
            Assists = players.Sum(x => x.Stats!.Assists),
            ShotsTaken = players.Sum(x => x.Stats!.ShotsTaken),
        };
        teamStats.SetApplicableValuesWithinObject();
        return teamStats;
    }

}