using Discord.WebSocket;
using EchoRankedServerBot.Models.Match;
using EchoRankedServerBot.Services;
using Microsoft.Extensions.Logging;

namespace EchoRankedServerBot.Handlers;

public class ChannelCreatedHandler(MatchStateService matchState, ILogger<ChannelCreatedHandler> logger)
{
    public Task HandleChannelCreatedAsync(SocketChannel channel)
    {
        if (channel is not SocketTextChannel textChannel || !textChannel.Name.StartsWith("queue-"))
            return Task.CompletedTask;

        var queueNumber = textChannel.Name.Split('-')[1];

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

        matchState.TryAdd(echoMatch);
        logger.LogInformation("Queue channel created: {ChannelName}", textChannel.Name);
        return Task.CompletedTask;
    }
}
