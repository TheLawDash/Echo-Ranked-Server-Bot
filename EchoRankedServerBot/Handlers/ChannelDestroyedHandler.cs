using Discord.WebSocket;
using EchoRankedServerBot.Services;
using Microsoft.Extensions.Logging;

namespace EchoRankedServerBot.Handlers;

public class ChannelDestroyedHandler(
    MatchStateService matchState,
    MatchLifecycleService lifecycle,
    ILogger<ChannelDestroyedHandler> logger)
{
    public async Task HandleChannelDestroyedAsync(SocketChannel channel)
    {
        if (channel is not SocketTextChannel textChannel)
        {
            logger.LogDebug("Ignoring destroyed channel {ChannelId} because it is not a text channel", channel.Id);
            return;
        }

        try
        {
            var rankedMatch = matchState.GetByChannelId(textChannel.Id);
            if (rankedMatch == null)
            {
                logger.LogDebug("Ignoring destroyed channel {ChannelId} ({ChannelName}) because it is not a tracked queue channel", textChannel.Id, textChannel.Name);
                return;
            }

            if (rankedMatch.EchoMatchInstance == null)
            {
                if (!matchState.TryRemove(rankedMatch.MatchId, out _))
                {
                    logger.LogWarning("Could not remove match state {MatchId} for destroyed channel {ChannelId} ({ChannelName})", rankedMatch.MatchId, textChannel.Id, textChannel.Name);
                }
                else
                {
                    logger.LogInformation("Queue channel {ChannelName} deleted before a match started, match {MatchId} removed", textChannel.Name, rankedMatch.MatchId);
                }

                return;
            }

            lifecycle.StopMatchMonitoring(rankedMatch.MatchId);
            logger.LogInformation("Queue channel deleted, match {MatchId} cleaned up", rankedMatch.MatchId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling channel deletion for {ChannelId}", textChannel.Id);
        }
    }
}
