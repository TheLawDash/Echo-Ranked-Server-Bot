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
        if (rankedMatch == null)
        {
            logger.LogWarning(
                "StartMonitoring: No ranked match found with ID {MatchId}, monitoring loops were not started",
                matchId);
            return;
        }

        var cts = new CancellationTokenSource();
        matchState.UpdateMatch(matchId, m => m.MonitoringCts = cts);

        _ = RunMainTransitionLoopAsync(matchId, cts.Token);
        _ = RunStatCheckLoopAsync(matchId, cts.Token);
        _ = RunPlayerJoinCheckAsync(matchId, cts.Token);

        logger.LogInformation("Started monitoring loops for match {MatchId}", matchId);
    }

    /// <summary>
    /// Attempts to extract the broadcaster session id, falling back to the portion of the
    /// broadcaster id before the first dot. Logs a warning if neither is usable.
    /// </summary>
    private bool TryGetSessionId(EchoMatchInstance instance, string matchId, string loopName, out string sessionId)
    {
        sessionId = string.Empty;

        if (!string.IsNullOrEmpty(instance.SessionId))
        {
            sessionId = instance.SessionId;
            return true;
        }

        var broadcasterId = instance.BroadcasterId;
        if (string.IsNullOrEmpty(broadcasterId))
        {
            logger.LogWarning(
                "{LoopName}: BroadcasterId was empty for match {MatchId}, cannot determine a session id",
                loopName, matchId);
            return false;
        }

        var parts = broadcasterId.Split('.');
        if (parts.Length == 0 || string.IsNullOrEmpty(parts[0]))
        {
            logger.LogWarning(
                "{LoopName}: Could not extract a session id from broadcaster id {BroadcasterId} for match {MatchId}",
                loopName, broadcasterId, matchId);
            return false;
        }

        sessionId = parts[0];
        return true;
    }

    private async Task RunMainTransitionLoopAsync(string matchId, CancellationToken ct)
    {
        const string loopName = "MainTransitionLoop";
        logger.LogInformation("{LoopName} started for match {MatchId}", loopName, matchId);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                try
                {
                    await MainTransitionTickAsync(matchId);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex,
                        "{LoopName}: Unhandled error during a tick for match {MatchId}. Continuing to the next tick.",
                        loopName, matchId);
                }
            }

            logger.LogInformation("{LoopName} exited normally for match {MatchId}", loopName, matchId);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation(
                "{LoopName} stopped for match {MatchId} because monitoring was cancelled",
                loopName, matchId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "{LoopName} for match {MatchId} stopped because of an unexpected error. The match is no longer being monitored.",
                loopName, matchId);
        }
    }

    private async Task MainTransitionTickAsync(string matchId)
    {
        const string loopName = "MainTransitionLoop";

        var rankedMatch = matchState.GetByMatchId(matchId);
        if (rankedMatch?.EchoMatchInstance == null)
        {
            logger.LogWarning(
                "{LoopName}: No ranked match or match instance found for match {MatchId}, skipping tick",
                loopName, matchId);
            return;
        }
        if (rankedMatch.EchoMatchInstance.PostingStats) return;

        var token = await nakamaApi.GetNakamaTokenAsync();
        if (token == null)
        {
            logger.LogWarning(
                "{LoopName}: Failed to retrieve Nakama token for match {MatchId}, skipping tick",
                loopName, matchId);
            return;
        }

        if (!TryGetSessionId(rankedMatch.EchoMatchInstance, matchId, loopName, out var sessionId))
            return;

        var echoMatch = await streamingApi.GetEchoApiFromStreamingAsync(sessionId, token);
        if (echoMatch == null)
        {
            logger.LogWarning(
                "{LoopName}: Streaming API returned no data for match {MatchId}, session {SessionId}. The match will no longer be monitored.",
                loopName, matchId, sessionId);
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

                    var rewarded = await neatQueue.RewardMvpAsync(player.MemberId, options.Value.NeatQueueChannelId);
                    if (!rewarded)
                    {
                        logger.LogWarning(
                            "{LoopName}: Failed to reward MVP MMR for Discord member {MemberId} in match {MatchId}, so no MMR was awarded.",
                            loopName, player.MemberId, matchId);
                    }

                    var liveChannel = discord.GetTextChannel(options.Value.LiveMatchesChannelId);
                    if (liveChannel == null)
                    {
                        logger.LogWarning(
                            "{LoopName}: Live matches channel {ChannelId} was not found for match {MatchId}, could not announce MVP reward.",
                            loopName, options.Value.LiveMatchesChannelId, matchId);
                    }
                    else if (rankedMatch.PrivateMatchDetails?.LiveMatchMessageId != null)
                    {
                        try
                        {
                            await liveChannel.SendMessageAsync($"`+7 MMR has been awarded for` <@{player.MemberId}>");
                        }
                        catch (Exception ex)
                        {
                            logger.LogError(ex,
                                "{LoopName}: Failed to send MVP reward message to channel {ChannelId} for match {MatchId}",
                                loopName, liveChannel.Id, matchId);
                        }
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
        const string loopName = "StatCheckLoop";
        logger.LogInformation("{LoopName} started for match {MatchId}", loopName, matchId);
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(10));
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                try
                {
                    await StatCheckTickAsync(matchId);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex,
                        "{LoopName}: Unhandled error during a tick for match {MatchId}. Continuing to the next tick.",
                        loopName, matchId);
                }
            }

            logger.LogInformation("{LoopName} exited normally for match {MatchId}", loopName, matchId);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation(
                "{LoopName} stopped for match {MatchId} because monitoring was cancelled",
                loopName, matchId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "{LoopName} for match {MatchId} stopped because of an unexpected error. The match is no longer being monitored.",
                loopName, matchId);
        }
    }

    private async Task StatCheckTickAsync(string matchId)
    {
        const string loopName = "StatCheckLoop";

        var rankedMatch = matchState.GetByMatchId(matchId);
        if (rankedMatch?.EchoMatchInstance == null)
        {
            logger.LogWarning(
                "{LoopName}: No ranked match or match instance found for match {MatchId}, skipping tick",
                loopName, matchId);
            return;
        }

        var token = await nakamaApi.GetNakamaTokenAsync();
        if (token == null)
        {
            logger.LogWarning(
                "{LoopName}: Failed to retrieve Nakama token for match {MatchId}, skipping tick",
                loopName, matchId);
            return;
        }

        if (!TryGetSessionId(rankedMatch.EchoMatchInstance, matchId, loopName, out var sessionId))
            return;

        var matchData = await streamingApi.GetEchoApiFromStreamingAsync(sessionId, token);
        if (matchData == null)
        {
            logger.LogWarning(
                "{LoopName}: Streaming API returned no data for match {MatchId}, session {SessionId}, skipping tick",
                loopName, matchId, sessionId);
            return;
        }

        try
        {
            var currentScores = rankedMatch.EchoMatchInstance.PlayerScores;
            if (currentScores.Count == 0) return;

            // Generate scoreboard image
            var templatePath = Path.Combine(AppContext.BaseDirectory, "Assets", "original.png");
            using var imageStream = scoreboard.GenerateScoreboardAsync(templatePath, matchData, currentScores, matchId);

            if (imageStream == null)
            {
                logger.LogWarning(
                    "{LoopName}: Scoreboard image generation returned null for match {MatchId}, live scoreboard was not updated.",
                    loopName, matchId);
                return;
            }

            // Update live match message
            var liveChannel = discord.GetTextChannel(options.Value.LiveMatchesChannelId);
            if (liveChannel == null)
            {
                logger.LogWarning(
                    "{LoopName}: Live matches channel {ChannelId} was not found for match {MatchId}, live scoreboard was not updated.",
                    loopName, options.Value.LiveMatchesChannelId, matchId);
                return;
            }

            if (rankedMatch.PrivateMatchDetails?.LiveMatchMessageId == null)
            {
                logger.LogWarning(
                    "{LoopName}: No live match message id recorded for match {MatchId}, live scoreboard was not updated.",
                    loopName, matchId);
                return;
            }

            var queueName = GetQueueName(rankedMatch);
            var embedBuilder = new EmbedBuilder()
                .WithColor(Color.Green)
                .WithTitle($"Match for: {queueName}")
                .AddField("Last updated at:", $"<t:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}:R>")
                .WithImageUrl($"attachment://{matchId}.png")
                .WithFooter("Echo Ranked • Server Manager");

            var liveMessageId = rankedMatch.PrivateMatchDetails.LiveMatchMessageId.Value;
            IUserMessage? existingMsg;
            try
            {
                existingMsg = await liveChannel.GetMessageAsync(liveMessageId) as IUserMessage;
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "{LoopName}: Failed to fetch live match message {MessageId} in channel {ChannelId} for match {MatchId}",
                    loopName, liveMessageId, liveChannel.Id, matchId);
                return;
            }

            if (existingMsg == null)
            {
                logger.LogWarning(
                    "{LoopName}: Live match message {MessageId} was not found in channel {ChannelId} for match {MatchId}, live scoreboard was not updated.",
                    loopName, liveMessageId, liveChannel.Id, matchId);
                return;
            }

            imageStream.Position = 0;
            try
            {
                await existingMsg.ModifyAsync(msg =>
                {
                    msg.Embed = embedBuilder.Build();
                    msg.Attachments = new[] { new FileAttachment(imageStream, $"{matchId}.png") };
                });
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "{LoopName}: Failed to update live match message {MessageId} in channel {ChannelId} for match {MatchId}",
                    loopName, liveMessageId, liveChannel.Id, matchId);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in stat check tick for {MatchId}", matchId);
        }
    }

    private async Task RunPlayerJoinCheckAsync(string matchId, CancellationToken ct)
    {
        const string loopName = "PlayerJoinCheck";
        logger.LogInformation("{LoopName} started for match {MatchId}", loopName, matchId);
        try
        {
            // Wait 5 minutes before first check
            await Task.Delay(TimeSpan.FromMinutes(5), ct);

            var rankedMatch = matchState.GetByMatchId(matchId);
            if (rankedMatch?.EchoMatchInstance == null)
            {
                logger.LogWarning(
                    "{LoopName}: No ranked match or match instance found for match {MatchId}, skipping check",
                    loopName, matchId);
                return;
            }

            var token = await nakamaApi.GetNakamaTokenAsync();
            if (token == null)
            {
                logger.LogWarning(
                    "{LoopName}: Failed to retrieve Nakama token for match {MatchId}, skipping check",
                    loopName, matchId);
                return;
            }

            if (!TryGetSessionId(rankedMatch.EchoMatchInstance, matchId, loopName, out var sessionId))
                return;

            var echoVr = await streamingApi.GetEchoApiFromStreamingAsync(sessionId, token);
            if (echoVr?.Teams == null)
            {
                logger.LogWarning(
                    "{LoopName}: Streaming API returned no team data for match {MatchId}, session {SessionId}, skipping check",
                    loopName, matchId, sessionId);
                return;
            }

            var teams = new List<Team>();
            if (echoVr.Teams.Count > 0) teams.Add(echoVr.Teams[0]);
            if (echoVr.Teams.Count > 1) teams.Add(echoVr.Teams[1]);

            foreach (var team in teams)
            {
                if (team is not { Players.Count: > 0, TeamName: not null } ||
                    team.TeamName.Contains("SPECTATOR")) continue;
                logger.LogInformation("Players joined match {MatchId}", matchId);
                matchState.UpdateMatch(matchId, m => m.PrivateMatchDetails!.MatchStarting = false);
                logger.LogInformation("{LoopName} exited normally for match {MatchId}", loopName, matchId);
                return;
            }

            // No players joined after 5 minutes - stop monitoring
            logger.LogWarning(
                "{LoopName}: No players joined match {MatchId} within 5 minutes. The match will no longer be monitored.",
                loopName, matchId);
            lifecycle.StopMatchMonitoring(matchId);
            logger.LogInformation("{LoopName} exited normally for match {MatchId}", loopName, matchId);
        }
        catch (OperationCanceledException)
        {
            logger.LogInformation(
                "{LoopName} stopped for match {MatchId} because monitoring was cancelled",
                loopName, matchId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "{LoopName} for match {MatchId} stopped because of an unexpected error. The match is no longer being monitored.",
                loopName, matchId);
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
        {
            logger.LogWarning(
                "GetListOfPlayersAsync: No ranked match or match instance found for match {MatchId}",
                matchId);
            return (details, echoMatch.LastScore);
        }

        try
        {
            var serverList = await nakamaApi.GetNakamaMatchesAsync(token);
            if (serverList == null)
            {
                logger.LogWarning(
                    "GetListOfPlayersAsync: Failed to retrieve Nakama matches for match {MatchId}, player details were not updated.",
                    matchId);
                return (details, echoMatch.LastScore);
            }

            var wantedMatch = serverList.Labels.Find(x =>
                x.Id.Contains(rankedMatch.EchoMatchInstance.BroadcasterId, StringComparison.OrdinalIgnoreCase));
            if (wantedMatch?.Players == null)
            {
                logger.LogWarning(
                    "GetListOfPlayersAsync: No Nakama match found for broadcaster {BroadcasterId} in match {MatchId}, player details were not updated.",
                    rankedMatch.EchoMatchInstance.BroadcasterId, matchId);
                return (details, echoMatch.LastScore);
            }

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
                        if (guild == null)
                        {
                            logger.LogWarning(
                                "GetListOfPlayersAsync: Guild {GuildId} was not found while resolving player {PlayerName} in match {MatchId}",
                                options.Value.GuildId, apiPlayer.Name, matchId);
                        }

                        if (!ulong.TryParse(nakamaPlayer.DiscordId, out var discordIdValue))
                        {
                            logger.LogWarning(
                                "GetListOfPlayersAsync: Discord ID {DiscordId} for player {PlayerName} in match {MatchId} could not be parsed as a ulong",
                                nakamaPlayer.DiscordId, apiPlayer.Name, matchId);
                        }

                        var member = guild?.GetUser(discordIdValue);

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
                        if (altChannel == null)
                        {
                            logger.LogWarning(
                                "GetListOfPlayersAsync: Alt channel {ChannelId} was not found, could not report watch detection for Discord ID {DiscordId} in match {MatchId}",
                                options.Value.AltChannelId, nakamaPlayer.DiscordId, matchId);
                        }
                        else
                        {
                            try
                            {
                                await altChannel.SendMessageAsync(
                                    $"**Player Detected:** `{nakamaPlayer.DisplayName}`\n" +
                                    $"**IP Address:** `{nakamaPlayer.ClientIp}`\n" +
                                    $"**Discord ID:** `{nakamaPlayer.DiscordId}`\n" +
                                    $"**Reason:** User was on an IP that was being watched for a different user.");
                            }
                            catch (Exception ex)
                            {
                                logger.LogError(ex,
                                    "GetListOfPlayersAsync: Failed to send watch detection message to channel {ChannelId} for Discord ID {DiscordId} in match {MatchId}",
                                    altChannel.Id, nakamaPlayer.DiscordId, matchId);
                            }
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
        if (match.PrivateMatchDetails == null)
        {
            logger.LogWarning(
                "GetQueueName: PrivateMatchDetails was null for match {MatchId}, using fallback name",
                match.MatchId);
            return "Unknown";
        }

        var channel = discord.GetTextChannel(match.PrivateMatchDetails.QueueChannelId);
        if (channel == null)
        {
            logger.LogWarning(
                "GetQueueName: Queue channel {ChannelId} was not found for match {MatchId}, using fallback name",
                match.PrivateMatchDetails.QueueChannelId, match.MatchId);
        }
        return channel?.Name ?? $"queue-{match.PrivateMatchDetails.QueueNumber}";
    }
}
