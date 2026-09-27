using System.Collections.Concurrent;
using Discord;
using Discord.WebSocket;
using EchoRankedServerBot.Configuration;
using EchoRankedServerBot.Models.EchoApi;
using EchoRankedServerBot.Models.Match;
using EchoRankedServerBot.Services;
using EchoRankedServerBot.Telemetry;
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
    LiveTelemetryService telemetry,
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
    private readonly ConcurrentDictionary<string, byte> _unnamedPlayersLogged = new();

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

        _unnamedPlayersLogged.TryRemove(matchId, out _);

        // Subscribe now rather than on the first tick: player names are only sent as players join
        if (rankedMatch.EchoMatchInstance != null &&
            TryGetSessionId(rankedMatch.EchoMatchInstance, matchId, "StartMonitoring", out var sessionId))
            telemetry.EnsureSubscribed(sessionId);

        var cts = new CancellationTokenSource();
        matchState.UpdateMatch(matchId, m =>
        {
            // Never run two sets of loops for one match, which would record its stats twice
            m.MonitoringCts?.Cancel();
            m.MonitoringCts = cts;
        });

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

        if (!TryGetSessionId(rankedMatch.EchoMatchInstance, matchId, loopName, out var sessionId))
            return;

        // No data yet means the server has not started streaming; the join check handles no-shows
        var echoMatch = GetTelemetry(sessionId);
        if (echoMatch == null)
        {
            logger.LogDebug(
                "{LoopName}: No telemetry yet for match {MatchId}, session {SessionId}, skipping tick",
                loopName, matchId, sessionId);
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

                lifecycle.StopMatchMonitoring(matchId, newInstance: true);
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

        if (!TryGetSessionId(rankedMatch.EchoMatchInstance, matchId, loopName, out var sessionId))
            return;

        var matchData = GetTelemetry(sessionId);
        if (matchData == null)
        {
            logger.LogDebug(
                "{LoopName}: No telemetry yet for match {MatchId}, session {SessionId}, skipping tick",
                loopName, matchId, sessionId);
            return;
        }

        try
        {
            var (currentScores, _) = lifecycle.GetPlayerScoreFromPlayers(matchData);
            if (currentScores.Count == 0)
            {
                var activePlayers = telemetry.GetActivePlayerCount(sessionId);
                if (activePlayers > 0 && _unnamedPlayersLogged.TryAdd(matchId, 0))
                {
                    logger.LogWarning(
                        "{LoopName}: Match {MatchId}, session {SessionId} has {PlayerCount} players but no player names, so the live scoreboard cannot be drawn. " +
                        "The telemetry subscription started after they joined and nevr-stream did not replay the roster.",
                        loopName, matchId, sessionId, activePlayers);
                }
                return;
            }

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
                .WithDescription(FormatGameStatus(matchData))
                .AddField("Score:", $"Blue {matchData.BluePoints ?? 0} - {matchData.OrangePoints ?? 0} Orange", true)
                .AddField("Rounds:", $"Blue {matchData.BlueRoundScore ?? 0} - {matchData.OrangeRoundScore ?? 0} Orange", true)
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

            if (!TryGetSessionId(rankedMatch.EchoMatchInstance, matchId, loopName, out var sessionId))
                return;

            var echoVr = GetTelemetry(sessionId);
            if (echoVr?.Teams == null)
            {
                logger.LogWarning(
                    "{LoopName}: No telemetry was received for match {MatchId}, session {SessionId} within 5 minutes. The match will no longer be monitored.",
                    loopName, matchId, sessionId);
                lifecycle.StopMatchMonitoring(matchId, newInstance: true);
                return;
            }

            // Players can be in the match without names when the subscription started after they joined
            var activePlayers = telemetry.GetActivePlayerCount(sessionId);
            if (activePlayers > 0)
            {
                logger.LogInformation("{PlayerCount} players are in match {MatchId}", activePlayers, matchId);
                matchState.UpdateMatch(matchId, m => m.PrivateMatchDetails!.MatchStarting = false);
                logger.LogInformation("{LoopName} exited normally for match {MatchId}", loopName, matchId);
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
            lifecycle.StopMatchMonitoring(matchId, newInstance: true);
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
            lifecycle.StopMatchMonitoring(matchId, newInstance: true);
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
                AddPlayersFromPreviousTick(details, playerList, rankedMatch.EchoMatchInstance);
                return (details, echoMatch.LastScore);
            }

            var wantedMatch = serverList.Labels.Find(x =>
                x.Id.Contains(rankedMatch.EchoMatchInstance.BroadcasterId, StringComparison.OrdinalIgnoreCase));
            if (wantedMatch?.Players == null)
            {
                logger.LogWarning(
                    "GetListOfPlayersAsync: No Nakama match found for broadcaster {BroadcasterId} in match {MatchId}, player details were not updated.",
                    rankedMatch.EchoMatchInstance.BroadcasterId, matchId);
                AddPlayersFromPreviousTick(details, playerList, rankedMatch.EchoMatchInstance);
                return (details, echoMatch.LastScore);
            }

            foreach (var nakamaPlayer in wantedMatch.Players)
            {
                foreach (var apiPlayer in playerList)
                {
                    try
                    {
                        if (!IsSameAccount(nakamaPlayer.EvrId, apiPlayer.UserId))
                            continue;

                        var isOrangeTeam = echoMatch.Teams?[1].Players?.Any(x => x.UserId == apiPlayer.UserId) ?? false;
                        var team = isOrangeTeam ? "orange" : "blue";

                        apiPlayer.Stats ??= new PlayerStats();

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

        AddPlayersFromPreviousTick(details, playerList, rankedMatch.EchoMatchInstance);
        return (details, echoMatch.LastScore);
    }

    private static void AddPlayersFromPreviousTick(List<DiscordPlayerDetails> details, List<Player> playerList, EchoMatchInstance instance)
    {
        foreach (var apiPlayer in playerList)
        {
            if (details.Any(d => d.Player?.UserId == apiPlayer.UserId)) continue;

            var previous = instance.PlayerDetails.Find(d => IsSameAccount(d.EvrId, apiPlayer.UserId));
            if (previous == null) continue;

            previous.Player = apiPlayer;
            details.Add(previous);
        }
    }

    private EchoVrApiSession? GetTelemetry(string sessionId)
    {
        telemetry.EnsureSubscribed(sessionId);
        return telemetry.TryGetSnapshot(sessionId);
    }

    private static bool IsSameAccount(string? evrId, long? accountNumber)
    {
        if (string.IsNullOrEmpty(evrId) || accountNumber == null) return false;

        var dash = evrId.LastIndexOf('-');
        return long.TryParse(dash >= 0 ? evrId[(dash + 1)..] : evrId, out var id) && id == accountNumber;
    }

    private static string FormatGameStatus(EchoVrApiSession session)
    {
        var status = session.GameStatus switch
        {
            "pre_match" => "Waiting to start",
            "round_start" => "Round starting",
            "playing" => "In play",
            "score" => "Goal scored",
            "round_over" => "Round over",
            "post_match" => "Match over",
            "pre_sudden_death" or "sudden_death" or "post_sudden_death" => "Overtime",
            _ => "Waiting for server"
        };

        return string.IsNullOrEmpty(session.GameClockDisplay) ? status : $"{status} • {session.GameClockDisplay}";
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
