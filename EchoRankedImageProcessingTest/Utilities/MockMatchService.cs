using EchoRankedServerBot.Models.EchoApi;
namespace EchoRankedImageProcessingTest.Utilities;

public static class MockMatchService
{
    public static EchoVrApiSession CreateMockEchoVrApiSession()
    {
        var blueTeam = new Team()
        {
            TeamName = "Blue"
        }.PopulateMockTeam();

        var orangeTeam = new Team
        {
            TeamName = "Orange"
        }.PopulateMockTeam();
        
        return new EchoVrApiSession
        {
            OrangePoints = orangeTeam.Stats!.Points,
            BluePoints = blueTeam.Stats!.Points,
            OrangeRoundScore = 1,
            BlueRoundScore = 2,
            Teams = [blueTeam, orangeTeam]
        };
    }
}