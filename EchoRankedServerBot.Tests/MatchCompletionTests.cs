using EchoRankedServerBot.Models.EchoApi;
using EchoRankedServerBot.Models.Match;
using EchoRankedServerBot.Services;
using EchoRankedServerBot.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;
using Nevr.Telemetry.V2;
using Xunit;

namespace EchoRankedServerBot.Tests;

public class MatchCompletionTests
{
    private static MatchLifecycleService Lifecycle() => new(
        null!, null!, null!, null!, null!, null!, NullLogger<MatchLifecycleService>.Instance);

    private static LiveMatchState NewState()
    {
        var state = new LiveMatchState();
        state.Apply(new Envelope
        {
            Header = new CaptureHeader
            {
                EchoArena = new EchoArenaHeader
                {
                    SessionId = "test-session", TotalRoundCount = 3,
                    InitialRoster =
                    {
                        new PlayerInfo { Slot = 0, AccountNumber = 123, DisplayName = "TestPlayer", Role = Role.BlueTeam }
                    }
                }
            }
        });
        return state;
    }

    private static void ApplyFrame(LiveMatchState state, GameStatus status, int blueRounds = 0, int orangeRounds = 0,
        params EchoEvent[] events)
    {
        var arena = new EchoArenaFrame { GameStatus = status, GameClock = 600 };
        arena.Events.Add(new EchoEvent
        {
            ScoreboardUpdated = new ScoreboardUpdated { BlueRoundScore = blueRounds, OrangeRoundScore = orangeRounds }
        });
        arena.Events.Add(events);
        state.Apply(new Envelope { Frame = new Frame { EchoArena = arena } });
    }

    private static EchoVrApiSession EndStream(LiveMatchState state)
    {
        state.Apply(new Envelope { Footer = new CaptureFooter() });
        return Assert.IsType<EchoVrApiSession>(state.ToSession());
    }

    [Fact]
    public void JoiningThenLeavingBeforeStartDoesNotProduceACompletedMatchOrMvp()
    {
        var state = NewState();
        ApplyFrame(state, GameStatus.PreMatch);
        ApplyFrame(state, GameStatus.PreMatch, events:
        [new EchoEvent { PlayerLeft = new PlayerLeft { PlayerSlot = 0 } }]);
        var session = EndStream(state);

        Assert.Equal("pre_match", session.GameStatus);
        Assert.True(session.TelemetryEnded);
        Assert.Equal("10:00.00", session.GameClockDisplay);
        var player = Assert.Single(session.Teams![0].Players!);
        Assert.True(player.PlayerLeft);
        Assert.True(MatchResultPolicy.IsCancelled(session));
        Assert.False(MatchResultPolicy.TryGetWinningTeam(session, out _));
        var lifecycle = Lifecycle();
        var (scores, _) = lifecycle.GetPlayerScoreFromPlayers(session);
        Assert.Null(lifecycle.GetMvp(scores));
    }

    [Theory]
    [InlineData(GameStatus.PreMatch, "pre_match")]
    [InlineData(GameStatus.RoundStart, "round_start")]
    [InlineData(GameStatus.Playing, "playing")]
    [InlineData(GameStatus.RoundOver, "round_over")]
    public void FooterDoesNotTurnAnIncompleteGameIntoPostMatch(GameStatus status, string expectedStatus)
    {
        var state = NewState();
        ApplyFrame(state, status, blueRounds: 1);
        var session = EndStream(state);

        Assert.Equal(expectedStatus, session.GameStatus);
        Assert.True(MatchResultPolicy.IsCancelled(session));
        Assert.False(MatchResultPolicy.TryGetWinningTeam(session, out _));
    }

    [Theory]
    [InlineData(2, 1, "blue")]
    [InlineData(1, 2, "orange")]
    [InlineData(1, 0, "blue")]
    [InlineData(0, 1, "orange")]
    public void GenuinePostMatchRemainsEligibleAfterStreamEnds(int blueRounds, int orangeRounds, string expectedWinner)
    {
        var state = NewState();
        ApplyFrame(state, GameStatus.PostMatch, blueRounds, orangeRounds);
        var session = EndStream(state);

        Assert.Equal("post_match", session.GameStatus);
        Assert.True(MatchResultPolicy.TryGetWinningTeam(session, out var winner));
        Assert.Equal(expectedWinner, winner);
        Assert.False(MatchResultPolicy.IsCancelled(session));
    }

    [Fact]
    public void MatchEndedEventCanCompleteAGameBeforeTheLastStatusFrameArrives()
    {
        var state = NewState();
        ApplyFrame(state, GameStatus.RoundOver, 2, 1,
            new EchoEvent { MatchEnded = new MatchEnded { WinningTeam = Role.BlueTeam } });
        var session = Assert.IsType<EchoVrApiSession>(state.ToSession());

        Assert.Equal("post_match", session.GameStatus);
        Assert.True(MatchResultPolicy.TryGetWinningTeam(session, out var winner));
        Assert.Equal("blue", winner);
    }

    [Fact]
    public void MatchEndedEventWithoutAnyCompletedRoundCannotAwardMmr()
    {
        var state = NewState();
        ApplyFrame(state, GameStatus.PreMatch, events:
        [new EchoEvent { MatchEnded = new MatchEnded { WinningTeam = Role.BlueTeam } }]);
        var session = Assert.IsType<EchoVrApiSession>(state.ToSession());

        Assert.True(MatchResultPolicy.IsCancelled(session));
        Assert.False(MatchResultPolicy.TryGetWinningTeam(session, out _));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(null, 0)]
    [InlineData(0, null)]
    [InlineData(-1, 0)]
    [InlineData(0, -1)]
    public void MissingTiedOrInvalidRoundScoresDoNotDefaultToABlueWin(int? blueRounds, int? orangeRounds)
    {
        var session = new EchoVrApiSession
        {
            GameStatus = "post_match", BlueRoundScore = blueRounds, OrangeRoundScore = orangeRounds
        };
        Assert.True(MatchResultPolicy.IsCancelled(session));
        Assert.False(MatchResultPolicy.TryGetWinningTeam(session, out var winner));
        Assert.Equal(string.Empty, winner);
    }

    [Fact]
    public void WaitingGameIsNotCancelledWhileTelemetryIsStillActive()
    {
        var state = NewState();
        ApplyFrame(state, GameStatus.PreMatch);
        var session = Assert.IsType<EchoVrApiSession>(state.ToSession());
        Assert.False(session.TelemetryEnded);
        Assert.False(MatchResultPolicy.IsCancelled(session));
        Assert.False(MatchResultPolicy.TryGetWinningTeam(session, out _));
    }

    [Fact]
    public void PositiveMvpScoreWinsOverEmptyOrInvalidScores()
    {
        var contributingPlayer = new Player { Name = "Contributor", UserId = 2 };
        List<PlayerScore> scores =
        [
            new() { Player = new Player { Name = "Idle", UserId = 1 }, Score = 0 },
            new() { Player = contributingPlayer, Score = 2 },
            new() { Player = new Player { Name = "Invalid", UserId = 3 }, Score = double.PositiveInfinity },
            new() { Player = null, Score = 10 }
        ];
        Assert.Same(contributingPlayer, Lifecycle().GetMvp(scores));
    }

    [Fact]
    public void AllZeroScoresHaveNoMvp()
    {
        List<PlayerScore> scores =
        [
            new() { Player = new Player { Name = "First" }, Score = 0 },
            new() { Player = new Player { Name = "Second" }, Score = 0 }
        ];
        Assert.Null(Lifecycle().GetMvp(scores));
    }
}
