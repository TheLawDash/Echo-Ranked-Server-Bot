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
            var queueNumber = channel.Name.Split('-')[1];

            // Find existing live match message
            var liveMatchesChannel = discord.GetTextChannel(options.Value.LiveMatchesChannelId);
            ulong? liveMessageId = null;
            if (liveMatchesChannel != null)
            {
                var messages = await liveMatchesChannel.GetMessagesAsync().FlattenAsync(); // Defaults to 100 messages, flatten since it's a readonly enumerable
                var embedMessage = messages
                    .FirstOrDefault(x => x.Embeds.Count > 0 && x.Embeds.Any(y => y.Title != null && y.Title.Contains(channel.Name)));
                liveMessageId = embedMessage?.Id;
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

            matchState.TryAdd(echoMatch);
        }

        await client.SetGameAsync("Echo VR Ranked");
        logger.LogInformation("Bot ready. Tracking {Count} queue channels", queueChannels.Count);
    }
}
