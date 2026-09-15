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
        {
            logger.LogDebug("Ignoring message {MessageId} because it is not a user message", message.Id);
            return;
        }

        if (message.Channel is not SocketTextChannel textChannel)
        {
            logger.LogDebug("Ignoring message {MessageId} because its channel is not a text channel", message.Id);
            return;
        }

        if (textChannel.Guild.Id != options.Value.GuildId)
        {
            logger.LogDebug("Ignoring message {MessageId} in channel {ChannelName} because it is not in the configured guild", message.Id, textChannel.Name);
            return;
        }

        if (textChannel.Id == options.Value.LogChannelId)
        {
            logger.LogDebug("Ignoring message {MessageId} because it was posted in the log channel {ChannelId}", message.Id, textChannel.Id);
            return;
        }

        var rankedMatch = matchState.GetByChannelId(textChannel.Id);
        if (rankedMatch?.PrivateMatchDetails == null)
        {
            logger.LogDebug("Ignoring message {MessageId} in channel {ChannelName} because it is not a tracked queue channel", message.Id, textChannel.Name);
            return;
        }

        // Only respond to NeatQueue bot messages
        if (message.Author.Id != options.Value.NeatQueueBotId)
        {
            logger.LogDebug("Ignoring message {MessageId} in channel {ChannelName} because it was not sent by the NeatQueue bot", message.Id, textChannel.Name);
            return;
        }

        // Check for queue pop embed (contains sword emoji)
        if (message.Embeds.Count <= 0 || !message.Embeds.Any(x => x.Title != null && x.Title.Contains('⚔')))
        {
            logger.LogDebug("Ignoring NeatQueue message {MessageId} in channel {ChannelName} because it does not contain a queue pop embed", message.Id, textChannel.Name);
            return;
        }

        logger.LogInformation("Handling NeatQueue message {MessageId} in channel {ChannelName}", message.Id, textChannel.Name);

        var matchId = rankedMatch.MatchId;
        matchState.UpdateMatch(matchId, m => m.PrivateMatchDetails!.MatchStarting = true);

        try
        {
            var embed = message.Embeds.First();
            if (embed.Fields.Length < 2)
            {
                logger.LogWarning("Queue pop embed for message {MessageId} in channel {ChannelName} did not have the expected two team fields, it had {FieldCount}", message.Id, textChannel.Name, embed.Fields.Length);
                matchState.UpdateMatch(matchId, m => m.PrivateMatchDetails!.MatchStarting = false);
                return;
            }

            var orange = embed.Fields[0].Value.Split(',');
            var blue = embed.Fields[1].Value.Split(',');

            var guild = textChannel.Guild;
            var (teamOrientations, rankedGameMembers) = await lifecycle.SetTeamOrientationsAsync(orange, blue, guild);

            var hasEu = rankedGameMembers?.Any(x => x.Roles.Any(y => y.Id == options.Value.EuRoleId)) ?? false;
            var matchCreated = await lifecycle.CreateRankedEchoMatchAsync(hasEu, teamOrientations, rankedMatch, rankedGameMembers!);

            if (matchCreated == null)
            {
                logger.LogWarning("Could not create an Echo match for message {MessageId} in channel {ChannelName}, notifying the channel of the server pull failure", message.Id, textChannel.Name);
                try
                {
                    await lifecycle.SendServerPullErrorAsync(textChannel);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to send the server pull error message to channel {ChannelId} ({ChannelName})", textChannel.Id, textChannel.Name);
                }

                matchState.UpdateMatch(matchId, m => m.PrivateMatchDetails!.MatchStarting = false);
                return;
            }

            var endpointParts = matchCreated.Broadcaster.Endpoint.Split(':');
            if (endpointParts.Length < 2)
            {
                logger.LogWarning("Could not parse the broadcaster endpoint {Endpoint} for match created from message {MessageId} in channel {ChannelName}", matchCreated.Broadcaster.Endpoint, message.Id, textChannel.Name);
                matchState.UpdateMatch(matchId, m => m.PrivateMatchDetails!.MatchStarting = false);
                return;
            }

            var ip = endpointParts[1];
            var location = await lifecycle.GetServerLocationAsync(ip);
            var matchMessageId = await lifecycle.SendServerMessageAsync(
                textChannel, ip, location, false, null,
                rankedMatch.PrivateMatchDetails.DecidedRegion,
                rankedMatch.PrivateMatchDetails.DecidedAverageLatency,
                rankedMatch.PrivateMatchDetails.PlayersUsedForDecision);

            var echoMatchId = lifecycle.GetMatchIdFromMatch(matchCreated);

            ulong? sparkLinkMessageId;
            try
            {
                var sparkLinkMsg = await textChannel.SendMessageAsync($"https://echo.taxi/spark://c/{echoMatchId}");
                sparkLinkMessageId = sparkLinkMsg.Id;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send the spark link message to channel {ChannelId} ({ChannelName}) for match {EchoMatchId}", textChannel.Id, textChannel.Name, echoMatchId);
                sparkLinkMessageId = null;
            }

            // Send live match message with scoreboard template
            ulong? liveMessageId = null;
            var liveChannel = discord.GetTextChannel(options.Value.LiveMatchesChannelId);
            if (liveChannel == null)
            {
                logger.LogWarning("Could not find the live matches channel {ChannelId}, so no live match message will be posted for {ChannelName}", options.Value.LiveMatchesChannelId, textChannel.Name);
            }
            else
            {
                try
                {
                    var templatePath = Path.Combine(AppContext.BaseDirectory, "Assets", "original.png");
                    await using var fileStream = new FileStream(templatePath, FileMode.Open, FileAccess.Read);
                    var embedBuilder = new EmbedBuilder()
                        .WithColor(Color.Green)
                        .WithTitle($"Match for: {textChannel.Name}")
                        .AddField("Last updated at:", $"<t:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}:R>")
                        .WithImageUrl("attachment://original.png")
                        .WithFooter("Echo Ranked • Server Manager");
                    var liveMsg = await liveChannel.SendFileAsync(fileStream, "original.png", embed: embedBuilder.Build());
                    liveMessageId = liveMsg.Id;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Failed to send the live match message to channel {ChannelId} for {ChannelName}, continuing without it", liveChannel.Id, textChannel.Name);
                }
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
                m.PrivateMatchDetails.SparkLinkMessageId = sparkLinkMessageId;
                m.PrivateMatchDetails.NeatQueueMessageId = message.Id;
                m.PrivateMatchDetails.MatchStarting = false;
            });

            // Start monitoring loops
            monitorCoordinator.StartMonitoring(matchId);

            logger.LogInformation("Match created for {Channel}: session {SessionId}", textChannel.Name, echoMatchId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error creating match for channel {Channel} from message {MessageId}", textChannel.Name, message.Id);
            matchState.UpdateMatch(matchId, m => m.PrivateMatchDetails!.MatchStarting = false);
        }
    }
}
