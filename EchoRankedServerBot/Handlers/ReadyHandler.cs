using Discord;
using Discord.WebSocket;
using EchoRankedServerBot.Configuration;
using EchoRankedServerBot.Models.Match;
using EchoRankedServerBot.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EchoRankedServerBot.Handlers;

public class ReadyHandler(
    DiscordSocketClient client,
    MatchStateService matchState,
    DiscordChannelService discord,
    IOptions<BotOptions> options,
    ILogger<ReadyHandler> logger)
{
    public async Task HandleReadyAsync()
    {
        var guild = client.GetGuild(options.Value.GuildId);
        if (guild == null)
        {
            logger.LogError("Guild {GuildId} not found", options.Value.GuildId);
            return;
        }

        var channels = guild.TextChannels;
        var queueChannels = channels.Where(x => x.Name.StartsWith("queue-")).ToList();

        logger.LogInformation("Found {Count} queue channels", queueChannels.Count);

        foreach (var channel in queueChannels)
        {
            try
            {
                var nameParts = channel.Name.Split('-');
                if (nameParts.Length < 2 || string.IsNullOrWhiteSpace(nameParts[1]))
                {
                    logger.LogWarning("Could not parse the queue number from channel name {ChannelName}, skipping this channel", channel.Name);
                    continue;
                }

                var queueNumber = nameParts[1];

                // Find existing live match message
                var liveMatchesChannel = discord.GetTextChannel(options.Value.LiveMatchesChannelId);
                ulong? liveMessageId = null;
                if (liveMatchesChannel == null)
                {
                    logger.LogWarning("Could not find the live matches channel {ChannelId}, so no existing live match message will be linked for {ChannelName}", options.Value.LiveMatchesChannelId, channel.Name);
                }
                else
                {
                    try
                    {
                        var messages = await liveMatchesChannel.GetMessagesAsync().FlattenAsync(); // Defaults to 100 messages, flatten since it's a readonly enumerable
                        var embedMessage = messages
                            .FirstOrDefault(x => x.Embeds.Count > 0 && x.Embeds.Any(y => y.Title != null && y.Title.Contains(channel.Name)));
                        liveMessageId = embedMessage?.Id;
                    }
                    catch (Exception ex)
                    {
                        logger.LogError(ex, "Failed to fetch messages from the live matches channel {ChannelId} while looking for an existing message for {ChannelName}", options.Value.LiveMatchesChannelId, channel.Name);
                    }
                }

                var echoMatch = new EchoMatch
                {
                    MatchId = Guid.NewGuid().ToString(),
                    PrivateMatchDetails = new PrivateMatchDetails
                    {
                        QueueNumber = queueNumber,
                        QueueChannelId = channel.Id,
                        MatchStarting = false,
                        StatsUploaded = false,
                        LiveMatchMessageId = liveMessageId
                    },
                    EchoMatchInstance = new EchoMatchInstance()
                };

                if (!matchState.TryAdd(echoMatch))
                {
                    logger.LogWarning("Could not register match state for queue channel {ChannelId} ({ChannelName}), it may already be tracked", channel.Id, channel.Name);
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to initialize tracking for queue channel {ChannelId} ({ChannelName}), continuing with the remaining channels", channel.Id, channel.Name);
            }
        }

        try
        {
            await client.SetGameAsync("Echo VR Ranked");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to set the bot's activity status, continuing startup anyway");
        }

        logger.LogInformation("Bot ready. Tracking {Count} queue channels", queueChannels.Count);
    }
}
