using Discord;
using Discord.WebSocket;
using EchoRankedServerBot.Configuration;
using EchoRankedServerBot.Models.EchoApi;
using EchoRankedServerBot.Models.Match;
using EchoRankedServerBot.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EchoRankedServerBot.BackgroundServices;

/// <summary>
/// Coordinates per-match monitoring loops. Each match gets its own set of
/// PeriodicTimer-based loops cancelled via CancellationToken.
/// </summary>
public class MatchMonitorCoordinator(
    MatchStateService matchState,
    MatchLifecycleService lifecycle,
    StreamingApiService streamingApi,
    NakamaApiService nakamaApi,
    StatsRepository statsRepo,
    NeatQueueService neatQueue,
    WatchService watchService,
    ScoreboardImageService scoreboard,
    DiscordChannelService discord,
    DiscordSocketClient client,
    IOptions<BotOptions> options,
    ILogger<MatchMonitorCoordinator> logger)
{
    public void StartMonitoring(string matchId)
    {
        var rankedMatch = matchState.GetByMatchId(matchId);
        if (rankedMatch == null) return;

        var cts = new CancellationTokenSource();
        matchState.UpdateMatch(matchId, m => m.MonitoringCts = cts);

        _ = RunMainTransitionLoopAsync(matchId, cts.Token);
        _ = RunStatCheckLoopAsync(matchId, cts.Token);
        _ = RunPlayerJoinCheckAsync(matchId, cts.Token);

        logger.LogInformation("Started monitoring loops for match {MatchId}", matchId);
    }

    private async Task RunMainTransitionLoopAsync(string matchId, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                await MainTransitionTickAsync(matchId);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            logger.LogError(ex, "MainTransitionLoop error for {MatchId}", matchId);
        }
    }

    private async Task MainTransitionTickAsync(string matchId)
    {
        var rankedMatch = matchState.GetByMatchId(matchId);
        if (rankedMatch?.EchoMatchInstance == null) return;
        if (rankedMatch.EchoMatchInstance.PostingStats) return;

        var token = await nakamaApi.GetNakamaTokenAsync();
        if (token == null) return;

        var sessionId = rankedMatch.EchoMatchInstance.SessionId
                        ?? rankedMatch.EchoMatchInstance.BroadcasterId.Split('.')[0];
        if (string.IsNullOrEmpty(sessionId)) return;

        var echoMatch = await streamingApi.GetEchoApiFromStreamingAsync(sessionId, token);
        if (echoMatch == null)
        {
            lifecycle.StopMatchMonitoring(matchId);
            return;
        }

        try
        {
            var (playerScores, playerList) = lifecycle.GetPlayerScoreFromPlayers(echoMatch);
            var mvpPlayer = lifecycle.GetMvp(playerScores);

            var (details, lastScored) = await GetListOfPlayersAsync(echoMatch, matchId, playerList, token);

            matchState.UpdateMatch(matchId, m =>
            {
                m.EchoMatchInstance!.Mvp = mvpPlayer;
                m.EchoMatchInstance.PlayerDetails = details;
                m.EchoMatchInstance.PlayerScores = playerScores;
                m.EchoMatchInstance.LastScore = lastScored;
            });

            var orangeWins = echoMatch.OrangeRoundScore > echoMatch.BlueRoundScore;
            var winningTeam = orangeWins ? "orange" : "blue";

            if (echoMatch.GameStatus == "post_match" && !rankedMatch.EchoMatchInstance.PostingStats)
            {
                matchState.UpdateMatch(matchId, m => m.EchoMatchInstance!.PostingStats = true);

                foreach (var player in details)
                {
                    var win = player.UserTeam?.ToLower() == winningTeam;
                    var stats = lifecycle.CreateStatsFromPlayer(player, GetQueueName(rankedMatch), playerScores, win);
                    await statsRepo.SaveMatchStatsAsync(stats);

                    if (mvpPlayer == null || player.Player?.UserId != mvpPlayer.UserId ||
                        player.Player?.Name != mvpPlayer.Name) continue;
                    if (player.MemberId == 0) continue;
                    await neatQueue.RewardMvpAsync(player.MemberId, options.Value.NeatQueueChannelId);
                    var liveChannel = discord.GetTextChannel(options.Value.LiveMatchesChannelId);
                    if (liveChannel != null && rankedMatch.PrivateMatchDetails?.LiveMatchMessageId != null)
                    {
                        await liveChannel.SendMessageAsync($"`+7 MMR has been awarded for` <@{player.MemberId}>");
                    }
                }

                lifecycle.StopMatchMonitoring(matchId);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in main transition tick for {MatchId}", matchId);
        }
    }

    private async Task RunStatCheckLoopAsync(string matchId, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                await StatCheckTickAsync(matchId);
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            logger.LogError(ex, "StatCheckLoop error for {MatchId}", matchId);
        }
    }

    private async Task StatCheckTickAsync(string matchId)
    {
        var rankedMatch = matchState.GetByMatchId(matchId);
        if (rankedMatch?.EchoMatchInstance == null) return;

        var token = await nakamaApi.GetNakamaTokenAsync();
        if (token == null) return;

        var sessionId = rankedMatch.EchoMatchInstance.SessionId
                        ?? rankedMatch.EchoMatchInstance.BroadcasterId.Split('.')[0];
        if (string.IsNullOrEmpty(sessionId)) return;

        var matchData = await streamingApi.GetEchoApiFromStreamingAsync(sessionId, token);
        if (matchData == null) return;

        try
        {
            var currentScores = rankedMatch.EchoMatchInstance.PlayerScores;
            if (currentScores.Count == 0) return;

            // Generate scoreboard image
            var templatePath = Path.Combine(AppContext.BaseDirectory, "Assets", "original.png");
            using var imageStream = scoreboard.GenerateScoreboardAsync(templatePath, matchData, currentScores, matchId);

            if (imageStream == null) return;

            // Update live match message
            var liveChannel = discord.GetTextChannel(options.Value.LiveMatchesChannelId);
            if (liveChannel == null || rankedMatch.PrivateMatchDetails?.LiveMatchMessageId == null)
                return;

            var queueName = GetQueueName(rankedMatch);
            var embedBuilder = new EmbedBuilder()
                .WithColor(Color.Green)
                .WithTitle($"Match for: {queueName}")
                .WithImageUrl($"attachment://{matchId}.png")
                .WithFooter($"Updated at {DateTime.Now:hh:mm tt}");

            var existingMsg = await liveChannel.GetMessageAsync(rankedMatch.PrivateMatchDetails.LiveMatchMessageId.Value) as IUserMessage;
            if (existingMsg != null)
            {
                imageStream.Position = 0;
                await existingMsg.ModifyAsync(msg =>
                {
                    msg.Embed = embedBuilder.Build();
                    msg.Attachments = new[] { new FileAttachment(imageStream, $"{matchId}.png") };
                });
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in stat check tick for {MatchId}", matchId);
        }
    }

    private async Task RunPlayerJoinCheckAsync(string matchId, CancellationToken ct)
    {
        try
        {
            // Wait 5 minutes before first check
            await Task.Delay(TimeSpan.FromMinutes(5), ct);

            var rankedMatch = matchState.GetByMatchId(matchId);
            if (rankedMatch?.EchoMatchInstance == null) return;

            var token = await nakamaApi.GetNakamaTokenAsync();
            if (token == null) return;

            var sessionId = rankedMatch.EchoMatchInstance.SessionId
                            ?? rankedMatch.EchoMatchInstance.BroadcasterId.Split('.')[0];
            if (string.IsNullOrEmpty(sessionId)) return;

            var echoVr = await streamingApi.GetEchoApiFromStreamingAsync(sessionId, token);
            if (echoVr?.Teams == null) return;

            var teams = new List<Team>();
            if (echoVr.Teams.Count > 0) teams.Add(echoVr.Teams[0]);
            if (echoVr.Teams.Count > 1) teams.Add(echoVr.Teams[1]);

            foreach (var team in teams)
            {
                if (team is not { Players.Count: > 0, TeamName: not null } ||
                    team.TeamName.Contains("SPECTATOR")) continue;
                logger.LogInformation("Players joined match {MatchId}", matchId);
                matchState.UpdateMatch(matchId, m => m.PrivateMatchDetails!.MatchStarting = false);
                return;
            }

            // No players joined after 5 minutes - stop monitoring
            lifecycle.StopMatchMonitoring(matchId);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            logger.LogError(ex, "PlayerJoinCheck error for {MatchId}", matchId);
            lifecycle.StopMatchMonitoring(matchId);
        }
    }

    private async Task<(List<DiscordPlayerDetails>, LastScoreInfo?)> GetListOfPlayersAsync(
        EchoVrApiSession echoMatch,
        string matchId,
        List<Player> playerList,
        Models.Nakama.TokenResponse token)
    {
        var details = new List<DiscordPlayerDetails>();
        var rankedMatch = matchState.GetByMatchId(matchId);
        if (rankedMatch?.EchoMatchInstance == null)
            return (details, echoMatch.LastScore);

        try
        {
            var serverList = await nakamaApi.GetNakamaMatchesAsync(token);
            if (serverList == null)
                return (details, echoMatch.LastScore);

            var wantedMatch = serverList.Labels.Find(x =>
                x.Id.Contains(rankedMatch.EchoMatchInstance.BroadcasterId, StringComparison.OrdinalIgnoreCase));
            if (wantedMatch?.Players == null)
                return (details, echoMatch.LastScore);

            var currentPlayers = rankedMatch.EchoMatchInstance.PlayerDetails;

            foreach (var nakamaPlayer in wantedMatch.Players)
            {
                foreach (var apiPlayer in playerList)
                {
                    try
                    {
                        if (!nakamaPlayer.EvrId.Contains(apiPlayer.UserId.ToString()!, StringComparison.OrdinalIgnoreCase))
                            continue;

                        var isOrangeTeam = echoMatch.Teams?[1].Players?.Any(x => x.UserId == apiPlayer.UserId) ?? false;
                        var team = isOrangeTeam ? "orange" : "blue";

                        apiPlayer.Stats ??= new PlayerStats();

                        // Carry over accumulated stats from previous ticks
                        var existing = currentPlayers.Find(x =>
                            x.EvrId != null && x.EvrId.Contains(apiPlayer.UserId.ToString()!, StringComparison.OrdinalIgnoreCase));
                        if (existing?.Player?.Stats != null)
                        {
                            apiPlayer.Stats.TwoPointShots = existing.Player.Stats.TwoPointShots;
                            apiPlayer.Stats.ThreePointShots = existing.Player.Stats.ThreePointShots;
                            apiPlayer.Stats.ShortBounceShots = existing.Player.Stats.ShortBounceShots;
                            apiPlayer.Stats.LongBounceShots = existing.Player.Stats.LongBounceShots;
                            apiPlayer.Stats.ThrowDistance = existing.Player.Stats.ThrowDistance;
                            apiPlayer.Stats.ShotSpeed = existing.Player.Stats.ShotSpeed;

                            if (echoMatch.LastScore != null
                                && echoMatch.LastScore != rankedMatch.EchoMatchInstance.LastScore
                                && echoMatch.LastScore.PersonScored == apiPlayer.Name)
                            {
                                apiPlayer.Stats.ShotSpeed.Add(echoMatch.LastScore.DiscSpeed);
                                apiPlayer.Stats.ThrowDistance.Add(echoMatch.LastScore.DistanceThrown);

                                var goalType = echoMatch.LastScore.GoalType ?? "";
                                if (goalType.Contains("bounce", StringComparison.OrdinalIgnoreCase))
                                {
                                    if (!goalType.Contains("long", StringComparison.OrdinalIgnoreCase))
                                    {
                                        apiPlayer.Stats.TwoPointShots += 1;
                                        apiPlayer.Stats.ShortBounceShots += 1;
                                    }
                                    else
                                    {
                                        apiPlayer.Stats.ThreePointShots += 1;
                                        apiPlayer.Stats.LongBounceShots += 1;
                                    }
                                }
                                if (goalType.Contains("Long", StringComparison.OrdinalIgnoreCase) && !goalType.Contains("Bounce", StringComparison.OrdinalIgnoreCase))
                                    apiPlayer.Stats.ThreePointShots += 1;
                                if (goalType.Contains("Two", StringComparison.OrdinalIgnoreCase) && !goalType.Contains("Bounce", StringComparison.OrdinalIgnoreCase))
                                    apiPlayer.Stats.TwoPointShots += 1;
                            }
                        }

                        var guild = client.GetGuild(options.Value.GuildId);
                        var member = guild?.GetUser(ulong.TryParse(nakamaPlayer.DiscordId, out var did) ? did : 0);

                        details.Add(new DiscordPlayerDetails
                        {
                            Player = apiPlayer,
                            Username = apiPlayer.Name,
                            EvrId = nakamaPlayer.EvrId,
                            UserIp = nakamaPlayer.ClientIp,
                            UserId = nakamaPlayer.UserId,
                            MemberId = member?.Id ?? 0,
                            DiscordId = nakamaPlayer.DiscordId,
                            UserTeam = team
                        });

                        // Check watch list
                        var detected = await watchService.CheckWatchAsync(nakamaPlayer.DiscordId, nakamaPlayer.ClientIp);
                        if (!detected) continue;
                        var altChannel = discord.GetTextChannel(options.Value.AltChannelId);
                        if (altChannel != null)
                        {
                            await altChannel.SendMessageAsync(
                                $"**Player Detected:** `{nakamaPlayer.DisplayName}`\n" +
                                $"**IP Address:** `{nakamaPlayer.ClientIp}`\n" +
                                $"**Discord ID:** `{nakamaPlayer.DiscordId}`\n" +
                                $"**Reason:** User was on an IP that was being watched for a different user.");
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Error processing player {PlayerName}", apiPlayer.Name);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in GetListOfPlayersAsync for {MatchId}", matchId);
        }

        return (details, echoMatch.LastScore);
    }

    private string GetQueueName(EchoMatch match)
    {
        if (match.PrivateMatchDetails == null) return "Unknown";
        var channel = discord.GetTextChannel(match.PrivateMatchDetails.QueueChannelId);
        return channel?.Name ?? $"queue-{match.PrivateMatchDetails.QueueNumber}";
    }
}
