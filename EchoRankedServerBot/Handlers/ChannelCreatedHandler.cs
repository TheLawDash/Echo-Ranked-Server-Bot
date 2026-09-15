using Discord.WebSocket;
using EchoRankedServerBot.Models.Match;
using EchoRankedServerBot.Services;
using Microsoft.Extensions.Logging;

namespace EchoRankedServerBot.Handlers;

public class ChannelCreatedHandler(MatchStateService matchState, ILogger<ChannelCreatedHandler> logger)
{
    public Task HandleChannelCreatedAsync(SocketChannel channel)
    {
        if (channel is not SocketTextChannel textChannel)
        {
            logger.LogDebug("Ignoring created channel {ChannelId} because it is not a text channel", channel.Id);
            return Task.CompletedTask;
        }

        if (!textChannel.Name.StartsWith("queue-"))
        {
            logger.LogDebug("Ignoring created channel {ChannelName} because it is not a queue channel", textChannel.Name);
            return Task.CompletedTask;
        }

        logger.LogInformation("Queue channel {ChannelName} was created, registering a new match", textChannel.Name);

        var nameParts = textChannel.Name.Split('-');
        if (nameParts.Length < 2 || string.IsNullOrWhiteSpace(nameParts[1]))
        {
            logger.LogWarning("Could not parse the queue number from channel name {ChannelName}, the match will not be registered", textChannel.Name);
            return Task.CompletedTask;
        }

        var queueNumber = nameParts[1];

        var echoMatch = new EchoMatch
        {
            MatchId = Guid.NewGuid().ToString(),
            PrivateMatchDetails = new PrivateMatchDetails
            {
                QueueNumber = queueNumber,
                QueueChannelId = textChannel.Id,
                MatchStarting = false,
                StatsUploaded = false
            },
            EchoMatchInstance = new EchoMatchInstance()
        };

        if (!matchState.TryAdd(echoMatch))
        {
            logger.LogWarning("Could not register match state for queue channel {ChannelId} ({ChannelName}), it may already be tracked", textChannel.Id, textChannel.Name);
            return Task.CompletedTask;
        }

        logger.LogInformation("Queue channel {ChannelName} registered successfully with match {MatchId}", textChannel.Name, echoMatch.MatchId);
        return Task.CompletedTask;
    }
}
