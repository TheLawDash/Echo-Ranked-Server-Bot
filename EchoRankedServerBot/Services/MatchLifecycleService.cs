using System.Text.Json;
using Discord;
using Discord.WebSocket;
using EchoRankedServerBot.Models.EchoApi;
using EchoRankedServerBot.Models.Match;
using EchoRankedServerBot.Models.Nakama;
using EchoRankedServerBot.Models.Stats;
using Microsoft.Extensions.Logging;

namespace EchoRankedServerBot.Services;

public class MatchLifecycleService(
    NakamaApiService nakamaApi,
    IpGeolocationService ipGeo,
    ServerDecisionService decision,
    MatchStateService matchState,
    DiscordChannelService discord,
    ILogger<MatchLifecycleService> logger)
{
    /// <summary>
    /// Resolves player mentions into TeamOrientation objects by looking up Nakama IDs,
    /// and resolves the corresponding SocketGuildUsers from the guild.
    /// </summary>
    public async Task<(List<TeamOrientation>?, List<SocketGuildUser>?)> SetTeamOrientationsAsync(
        string[] orange, string[] blue, SocketGuild guild)
    {
        var token = await nakamaApi.GetNakamaTokenAsync();
        if (token is null)
        {
            logger.LogError("Nakama token was null in SetTeamOrientationsAsync");
            await discord.LogInfoAsync($"{DiscordChannelService.GetDiscordTimestamp()} `Nakama token was null in SetTeamOrientationsAsync`");
            return (null, null);
        }

        var teamOrientations = new List<TeamOrientation>();
        var responseMembers = new List<SocketGuildUser>();

        foreach (var player in orange)
        {
            var discordId = player.Split('@')[1].Split('>')[0];
            var nakamaId = await nakamaApi.GetNakamaIdAsync(discordId, token);
            if (nakamaId is null)
                continue;

            teamOrientations.Add(new TeamOrientation
            {
                DiscordId = discordId,
                TeamName = "orange",
                NakamaId = nakamaId
            });

            var user = guild.GetUser(ulong.Parse(discordId));
            if (user is not null)
                responseMembers.Add(user);
        }

        foreach (var player in blue)
        {
            var discordId = player.Split('@')[1].Split('>')[0];
            var nakamaId = await nakamaApi.GetNakamaIdAsync(discordId, token);
            if (nakamaId is null)
                continue;

            teamOrientations.Add(new TeamOrientation
            {
                DiscordId = discordId,
                TeamName = "blue",
                NakamaId = nakamaId
            });

            var user = guild.GetUser(ulong.Parse(discordId));
            if (user is not null)
                responseMembers.Add(user);
        }

        return (teamOrientations, responseMembers);
    }

    /// <summary>
    /// Creates a ranked Echo match: decides the best server, prepares the match on Nakama,
    /// assigns players to their teams, and DMs each player.
    /// </summary>
    public async Task<MatchLabel?> CreateRankedEchoMatchAsync(
        bool containsEu,
        List<TeamOrientation>? players,
        EchoMatch rankedMatch,
        List<SocketGuildUser> discordPlayers)
    {
        var token = await nakamaApi.GetNakamaTokenAsync();
        if (token is null)
        {
            logger.LogError("Failed to retrieve Nakama token in CreateRankedEchoMatchAsync");
            await discord.LogInfoAsync("Failed to retrieve Nakama token in CreateRankedEchoMatchAsync.");
            return null;
        }

        var echoMatches = await nakamaApi.GetNakamaMatchesAsync(token);
        if (echoMatches is null)
        {
            logger.LogError("Failed to retrieve Nakama matches in CreateRankedEchoMatchAsync");
            await discord.LogInfoAsync("Failed to retrieve Nakama matches in CreateRankedEchoMatchAsync.");
            return null;
        }

        ServerDecisionResult? serverDecision = null;
        if (players is not null && players.Count != 0)
        {
            serverDecision = await decision.DecideBestServerAsync(echoMatches, players, token);
        }
        else
        {
            logger.LogInformation("Latency-based server decision skipped (no players provided)");
            await discord.LogInfoAsync("Latency-based server decision skipped (no players provided).");
        }

        var echoMatch = serverDecision?.Match ?? nakamaApi.GetEmptyEchoMatchAsync(echoMatches, containsEu);

        if (serverDecision is null)
        {
            logger.LogInformation("Latency data not available for selection, using default server ordering");
            await discord.LogInfoAsync("Latency data not available for selection, using default server ordering.");
        }

        if (echoMatch is null)
        {
            logger.LogError("Failed to find an empty Echo match in CreateRankedEchoMatchAsync");
            await discord.LogInfoAsync("Failed to find an empty Echo match in CreateRankedEchoMatchAsync.");
            return null;
        }

        if (rankedMatch.PrivateMatchDetails is null)
        {
            logger.LogError("Invalid private match details in CreateRankedEchoMatchAsync");
            await discord.LogInfoAsync("Invalid private match details in CreateRankedEchoMatchAsync.");
            return null;
        }

        var regionLabel = serverDecision?.RegionLabel
                          ?? ServerDecisionService.GetRegionLabel(echoMatch.Broadcaster.RegionCodes);
        var regionCode = serverDecision?.RegionCode
                         ?? echoMatch.Broadcaster.RegionCodes.FirstOrDefault();

        rankedMatch.PrivateMatchDetails.DecidedRegion = regionCode is not null
            ? $"{regionLabel} ({regionCode})"
            : regionLabel;
        rankedMatch.PrivateMatchDetails.DecidedAverageLatency = serverDecision?.AverageLatency;
        rankedMatch.PrivateMatchDetails.PlayersUsedForDecision = serverDecision?.PlayersUsed;

        var queueChannel = discord.GetTextChannel(rankedMatch.PrivateMatchDetails.QueueChannelId);
        var queueName = queueChannel?.Name ?? "unknown-queue";

        var matchId = await nakamaApi.PrepareEchoMatchAsync(echoMatch, token, players, queueName);
        if (matchId is null)
        {
            logger.LogWarning("Failed to prepare Echo match. Attempting backup");
            await discord.LogInfoAsync("Failed to prepare Echo match in CreateRankedEchoMatchAsync. Attempting backup.");

            matchId = await nakamaApi.BackupPrepareEchoMatchAsync(echoMatch, token, players, queueName);
            if (matchId is null)
            {
                logger.LogError("Backup preparation also failed in CreateRankedEchoMatchAsync");
                await discord.LogInfoAsync("Backup preparation also failed in CreateRankedEchoMatchAsync.");
                return null;
            }
        }

        if (players is null) return echoMatch;
        foreach (var player in players)
        {
            if (player.NakamaId is null || player.TeamName is null || player.DiscordId is null)
                continue;

            var (result, responseMessage) = await nakamaApi.AssignPlayersToEchoMatchAsync(
                player.NakamaId, echoMatch.Id, token, player.TeamName);

            if (!result)
            {
                logger.LogWarning(
                    "Failed to assign <@{DiscordId}> to {QueueName}. Response: {Response}",
                    player.DiscordId, queueName, responseMessage);
                await discord.LogInfoAsync(
                    $"{DiscordChannelService.GetDiscordTimestamp()} `Failed to assign <@{player.DiscordId}> to {queueName}, response:`\n```json\n{responseMessage}\n```");
                continue;
            }

            var discordPlayer = discordPlayers.FirstOrDefault(x => x.Id == ulong.Parse(player.DiscordId));
            try
            {
                if (discordPlayer is not null)
                {
                    await discordPlayer.SendMessageAsync(
                        $"Your match for {queueName} has been created, please open Echo and press play to join.");
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to DM player <@{DiscordId}> for {QueueName}", player.DiscordId, queueName);
                await discord.LogInfoAsync(
                    $"{DiscordChannelService.GetDiscordTimestamp()} `Failed to send message to <@{player.DiscordId}> for {queueName}`");
            }
        }

        return echoMatch;
    }

    /// <summary>
    /// Builds a PostStatsRequest from a player's accumulated match data.
    /// Accumulates stats from BeforeLeave snapshots. Determines MVP status and score.
    /// Shot speed and throw distance are serialized as JSON arrays.
    /// </summary>
    public PostStatsRequest CreateStatsFromPlayer(
        DiscordPlayerDetails player,
        string channelName,
        List<PlayerScore> playerScores,
        bool win)
    {
        if (player.Player?.Stats is null)
            throw new ArgumentException("Player or player stats cannot be null", nameof(player));

        var points = player.Player.Stats.Points;
        var saves = player.Player.Stats.Saves;
        var assists = player.Player.Stats.Assists;
        var possessionTime = player.Player.Stats.PossessionTime;
        var stuns = player.Player.Stats.Stuns;
        var passes = player.Player.Stats.Passes;
        var catches = player.Player.Stats.Catches;
        var steals = player.Player.Stats.Steals;
        var blocks = player.Player.Stats.Blocks;
        var interceptions = player.Player.Stats.Interceptions;
        var goals = player.Player.Stats.Goals;
        var shotsTaken = player.Player.Stats.ShotsTaken;
        var threePointShots = player.Player.Stats.ThreePointShots;
        var longBounceShots = player.Player.Stats.LongBounceShots;
        var twoPointShots = player.Player.Stats.TwoPointShots;
        var shortBounceShots = player.Player.Stats.ShortBounceShots;

        if (player.BeforeLeave?.Any() == true)
        {
            foreach (var previous in player.BeforeLeave.Where(p => p.Stats is not null))
            {
                points += previous.Stats!.Points;
                saves += previous.Stats.Saves;
                assists += previous.Stats.Assists;
                possessionTime += previous.Stats.PossessionTime;
                stuns += previous.Stats.Stuns;
                passes += previous.Stats.Passes;
                catches += previous.Stats.Catches;
                steals += previous.Stats.Steals;
                blocks += previous.Stats.Blocks;
                interceptions += previous.Stats.Interceptions;
                goals += previous.Stats.Goals;
                shotsTaken += previous.Stats.ShotsTaken;
            }
        }

        var mvpPlayer = GetMvp(playerScores);
        var playerScoreEntry = playerScores
            .FirstOrDefault(x => x.Player?.UserId == player.Player.UserId && x.Player?.Name == player.Player.Name);
        var wasMvp = playerScoreEntry?.Player == mvpPlayer;

        var shotSpeed = JsonSerializer.Serialize(player.Player.Stats.ShotSpeed);
        var throwDistance = JsonSerializer.Serialize(player.Player.Stats.ThrowDistance);

        return new PostStatsRequest
        {
            PlayerName = player.Player.Name ?? "Unknown",
            PlayerId = player.Player.UserId ?? 0,
            Points = points,
            Saves = saves,
            Assists = assists,
            PossessionTime = possessionTime,
            Stuns = stuns,
            Passes = passes,
            Catches = catches,
            Steals = steals,
            Blocks = blocks,
            Interceptions = interceptions,
            Goals = goals,
            ShotsTaken = shotsTaken,
            ThreePointShots = threePointShots,
            TwoPointShots = twoPointShots,
            LongBounceShots = longBounceShots,
            ShortBounceShots = shortBounceShots,
            ShotSpeed = shotSpeed,
            ThrowDistance = throwDistance,
            PrivateName = channelName,
            TimeStamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            UserIp = player.UserIp,
            EvrId = player.EvrId,
            UserId = player.UserId,
            DiscordUsername = player.Username,
            DiscordId = player.DiscordId,
            Mvp = wasMvp,
            MvpScore = playerScoreEntry?.Score ?? 0,
            Win = win,
            Lose = !win
        };
    }

    /// <summary>
    /// Calculates a weighted MVP score for each player in the match session.
    /// Returns the scored list and a flat list of all players from both teams.
    /// </summary>
    public (List<PlayerScore>, List<Player>) GetPlayerScoreFromPlayers(EchoVrApiSession echoMatch)
    {
        var playerScores = new List<PlayerScore>();
        var totalPlayers = new List<Player>();

        try
        {
            if (echoMatch.Teams is not null && echoMatch.Teams.Count >= 2)
            {
                totalPlayers.AddRange(echoMatch.Teams[0].Players ?? []);
                totalPlayers.AddRange(echoMatch.Teams[1].Players ?? []);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error while aggregating players in GetPlayerScoreFromPlayers");
        }

        const double weightPoints = 1.0;
        const double weightSaves = 1.0;
        const double weightAssists = 0.8;
        const double weightPossessionTime = 0.2;
        const double weightStuns = 0.5;
        const double weightPasses = 0.5;
        const double weightCatches = 0.3;
        const double weightSteals = 0.5;
        const double weightBlocks = 0.5;
        const double weightInterceptions = 0.5;
        const double weightShotsTaken = 0.8;

        foreach (var player in totalPlayers)
        {
            try
            {
                if (player.Stats is null)
                    continue;

                var score = player.Stats.Points * weightPoints +
                            player.Stats.Saves * weightSaves +
                            player.Stats.Assists * weightAssists +
                            player.Stats.PossessionTime * weightPossessionTime +
                            player.Stats.Stuns * weightStuns +
                            player.Stats.Passes * weightPasses +
                            player.Stats.Catches * weightCatches +
                            player.Stats.Steals * weightSteals +
                            player.Stats.Blocks * weightBlocks +
                            player.Stats.Interceptions * weightInterceptions;

                if (player.Stats.ShotsTaken > 0)
                {
                    score += 1.0 / (player.Stats.ShotsTaken + 1) * weightShotsTaken;
                }

                playerScores.Add(new PlayerScore { Player = player, Score = score });
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error calculating score for player {PlayerName}", player.Name);
            }
        }

        return (playerScores, totalPlayers);
    }

    /// <summary>
    /// Returns the player with the highest MVP score, or null if no scores exist.
    /// </summary>
    public Player? GetMvp(List<PlayerScore> playerScores)
    {
        try
        {
            return playerScores.OrderByDescending(p => p.Score).FirstOrDefault()?.Player;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error determining MVP");
            return null;
        }
    }

    /// <summary>
    /// Looks up the geographic location of a server IP.
    /// Forwards to IpGeolocationService.
    /// </summary>
    public Task<string> GetServerLocationAsync(string ip)
    {
        return ipGeo.GetServerLocationAsync(ip);
    }

    /// <summary>
    /// Extracts the short match ID from a MatchLabel (the portion before the first dot, uppercased).
    /// </summary>
    public string GetMatchIdFromMatch(MatchLabel echoMatch)
    {
        try
        {
            return echoMatch.Id.Split('.')[0].ToUpper();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting match ID from match label {MatchLabelId}", echoMatch.Id);
            return string.Empty;
        }
    }

    /// <summary>
    /// Sends or updates an embed with server reservation details. Returns the message ID.
    /// </summary>
    public async Task<ulong> SendServerMessageAsync(
        SocketTextChannel channel,
        string ip,
        string location,
        bool update,
        ulong? existingMessageId,
        string? decidedRegion,
        double? averageLatency,
        int? playersUsed)
    {
        try
        {
            const string format = "hh:mm tt";
            var sessionExpiry = DateTime.Now.AddMinutes(5).ToString(format);

            var regionInfo = string.IsNullOrWhiteSpace(decidedRegion)
                ? string.Empty
                : $"Selected Region: {decidedRegion}\n\n";

            var latencyInfo = string.Empty;
            if (averageLatency.HasValue)
            {
                latencyInfo = $"Estimated Avg Latency: {averageLatency.Value:F1}ms";
                if (playersUsed.HasValue)
                    latencyInfo += $" across {playersUsed} players";
                latencyInfo += "\n\n";
            }

            var embed = new EmbedBuilder()
                .WithColor(Color.Green)
                .WithTitle("Server has been reserved!")
                .WithDescription(
                    $"Config: Nakama Global Config\n\n" +
                    $"Server IP: {ip}\n\n" +
                    $"Server Location: {location}\n\n" +
                    regionInfo +
                    latencyInfo +
                    $"Please open echo, and click \"Play\" or go to a matchmaking terminal and hit \"Find Match\" to join!\n\n" +
                    $"Your session will be held until: `{sessionExpiry} EST`\n\n")
                .WithThumbnailUrl("https://cdn.discordapp.com/attachments/1230261297287794950/1230563467606360064/EchoRanked.png")
                .WithFooter($"Today at {sessionExpiry}")
                .Build();

            if (update && existingMessageId.HasValue)
            {
                if (await channel.GetMessageAsync(existingMessageId.Value) is IUserMessage existingMessage)
                {
                    await existingMessage.ModifyAsync(msg => msg.Embed = embed);
                    return existingMessage.Id;
                }
            }

            var message = await channel.SendMessageAsync(embed: embed);
            return message.Id;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception while sending server message. IP: {Ip}, Location: {Location}", ip, location);
            throw;
        }
    }

    /// <summary>
    /// Sends an error embed indicating the server could not be pulled.
    /// </summary>
    public async Task SendServerPullErrorAsync(SocketTextChannel channel)
    {
        try
        {
            const string format = "hh:mm tt";
            var embed = new EmbedBuilder()
                .WithColor(Color.Red)
                .WithTitle("There was an error pulling the server!")
                .WithDescription("Please pull your own server for this match.")
                .WithThumbnailUrl("https://cdn.discordapp.com/attachments/1230261297287794950/1230563467606360064/EchoRanked.png")
                .WithFooter($"Today at {DateTime.Now.AddMinutes(5).ToString(format)}")
                .Build();

            await channel.SendMessageAsync(embed: embed);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception while sending server pull error message");
        }
    }

    /// <summary>
    /// Stops monitoring a match. Cancels the monitoring CancellationTokenSource.
    /// If newInstance is true, resets the EchoMatchInstance for reuse.
    /// Otherwise, removes the match from MatchStateService entirely.
    /// </summary>
    public bool StopMatchMonitoring(string matchId, bool newInstance = false)
    {
        try
        {
            var rankedMatch = matchState.GetByMatchId(matchId);
            if (rankedMatch is null)
            {
                logger.LogWarning("StopMatchMonitoring: No ranked match found with ID {MatchId}", matchId);
                return false;
            }

            try
            {
                rankedMatch.MonitoringCts?.Cancel();
                rankedMatch.MonitoringCts?.Dispose();
                rankedMatch.MonitoringCts = null;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "StopMatchMonitoring: Failed to cancel/dispose MonitoringCts for match {MatchId}", matchId);
            }

            if (newInstance)
            {
                rankedMatch.EchoMatchInstance = new EchoMatchInstance();
                logger.LogInformation(
                    "StopMatchMonitoring: Match monitoring for {MatchId} stopped and replaced with a new instance", matchId);
                return true;
            }

            matchState.TryRemove(matchId, out _);
            logger.LogInformation("StopMatchMonitoring: Match monitoring for {MatchId} stopped and removed", matchId);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "StopMatchMonitoring: General error while stopping match monitoring for {MatchId}", matchId);
            return false;
        }
    }
}
