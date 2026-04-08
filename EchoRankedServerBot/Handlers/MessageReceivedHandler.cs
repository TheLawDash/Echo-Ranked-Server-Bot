using Discord;
using Discord.WebSocket;
using EchoRankedServerBot.Configuration;
using EchoRankedServerBot.Models.Match;
using EchoRankedServerBot.BackgroundServices;
using EchoRankedServerBot.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EchoRankedServerBot.Handlers;

public class MessageReceivedHandler(
    MatchStateService matchState,
    MatchLifecycleService lifecycle,
    DiscordChannelService discord,
    MatchMonitorCoordinator monitorCoordinator,
    IOptions<BotOptions> options,
    ILogger<MessageReceivedHandler> logger)
{
    public async Task HandleMessageReceivedAsync(SocketMessage message)
    {
        if (message is not SocketUserMessage)
            return;

        if (message.Channel is not SocketTextChannel textChannel)
            return;

        if (textChannel.Guild.Id != options.Value.GuildId)
            return;

        if (textChannel.Id == options.Value.LogChannelId)
            return;

        var rankedMatch = matchState.GetByChannelId(textChannel.Id);
        if (rankedMatch?.PrivateMatchDetails == null)
            return;

        // Only respond to NeatQueue bot messages
        if (message.Author.Id != options.Value.NeatQueueBotId)
            return;

        // Check for queue pop embed (contains sword emoji)
        if (message.Embeds.Count <= 0 || !message.Embeds.Any(x => x.Title != null && x.Title.Contains('⚔')))
            return;

        var matchId = rankedMatch.MatchId;
        matchState.UpdateMatch(matchId, m => m.PrivateMatchDetails!.MatchStarting = true);

        try
        {
            var embed = message.Embeds.First();
            var orange = embed.Fields[0].Value.Split(',');
            var blue = embed.Fields[1].Value.Split(',');

            var guild = textChannel.Guild;
            var (teamOrientations, rankedGameMembers) = await lifecycle.SetTeamOrientationsAsync(orange, blue, guild);

            var hasEu = rankedGameMembers?.Any(x => x.Roles.Any(y => y.Id == options.Value.EuRoleId)) ?? false;
            var matchCreated = await lifecycle.CreateRankedEchoMatchAsync(hasEu, teamOrientations, rankedMatch, rankedGameMembers!);

            if (matchCreated == null)
            {
                await lifecycle.SendServerPullErrorAsync(textChannel);
                return;
            }

            var ip = matchCreated.Broadcaster.Endpoint.Split(':')[1];
            var location = await lifecycle.GetServerLocationAsync(ip);
            var matchMessageId = await lifecycle.SendServerMessageAsync(
                textChannel, ip, location, false, null,
                rankedMatch.PrivateMatchDetails.DecidedRegion,
                rankedMatch.PrivateMatchDetails.DecidedAverageLatency,
                rankedMatch.PrivateMatchDetails.PlayersUsedForDecision);

            var echoMatchId = lifecycle.GetMatchIdFromMatch(matchCreated);
            var sparkLinkMsg = await textChannel.SendMessageAsync($"https://echo.taxi/spark://c/{echoMatchId}");

            // Send live match message with scoreboard template
            ulong? liveMessageId = null;
            var liveChannel = discord.GetTextChannel(options.Value.LiveMatchesChannelId);
            if (liveChannel != null)
            {
                var templatePath = Path.Combine(AppContext.BaseDirectory, "Assets", "original.png");
                await using var fileStream = new FileStream(templatePath, FileMode.Open, FileAccess.Read);
                var embedBuilder = new EmbedBuilder()
                    .WithColor(Color.Green)
                    .WithTitle($"Match for: {textChannel.Name}")
                    .WithImageUrl("attachment://original.png")
                    .WithFooter($"Updated at {DateTime.Now:hh:mm tt}");
                var liveMsg = await liveChannel.SendFileAsync(fileStream, "original.png", embed: embedBuilder.Build());
                liveMessageId = liveMsg.Id;
            }

            // Update match state
            matchState.UpdateMatch(matchId, m =>
            {
                m.EchoMatchInstance = new EchoMatchInstance
                {
                    StartedTime = DateTime.Now,
                    BroadcasterId = matchCreated.Id,
                    SessionId = echoMatchId
                };
                m.PrivateMatchDetails!.LiveMatchMessageId = liveMessageId;
                m.PrivateMatchDetails.MatchMessageId = matchMessageId;
                m.PrivateMatchDetails.SparkLinkMessageId = sparkLinkMsg.Id;
                m.PrivateMatchDetails.NeatQueueMessageId = message.Id;
                m.PrivateMatchDetails.MatchStarting = false;
            });

            // Start monitoring loops
            monitorCoordinator.StartMonitoring(matchId);

            logger.LogInformation("Match created for {Channel}: session {SessionId}", textChannel.Name, echoMatchId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating match for channel {Channel}", textChannel.Name);
            await discord.LogErrorAsync($"Error creating match: {ex.Message}");
        }
    }
}
