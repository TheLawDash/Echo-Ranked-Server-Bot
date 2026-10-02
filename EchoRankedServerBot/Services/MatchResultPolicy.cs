using EchoRankedServerBot.Models.EchoApi;

namespace EchoRankedServerBot.Services;

public static class MatchResultPolicy
{
    public static bool TryGetWinningTeam(EchoVrApiSession session, out string winningTeam)
    {
        winningTeam = string.Empty;
        if (session.GameStatus != "post_match" ||
            session.BlueRoundScore is not int blueRounds || blueRounds < 0 ||
            session.OrangeRoundScore is not int orangeRounds || orangeRounds < 0 ||
            blueRounds == orangeRounds)
            return false;

        // A non-tied, non-negative round result proves that at least one round was completed.
        winningTeam = blueRounds > orangeRounds ? "blue" : "orange";
        return true;
    }

    public static bool IsCancelled(EchoVrApiSession session) =>
        (session.TelemetryEnded || session.GameStatus == "post_match") &&
        !TryGetWinningTeam(session, out _);
}
