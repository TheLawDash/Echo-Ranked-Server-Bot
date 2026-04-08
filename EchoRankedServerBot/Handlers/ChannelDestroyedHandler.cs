using Discord.WebSocket;
using EchoRankedServerBot.Services;
using Microsoft.Extensions.Logging;

namespace EchoRankedServerBot.Handlers;

public class ChannelDestroyedHandler(
    MatchStateService matchState,
    MatchLifecycleService lifecycle,
    DiscordChannelService discord,
    ILogger<ChannelDestroyedHandler> logger)
{
    public async Task HandleChannelDestroyedAsync(SocketChannel channel)
    {
        if (channel is not SocketTextChannel textChannel)
            return;

        try
        {
            var rankedMatch = matchState.GetByChannelId(textChannel.Id);
            if (rankedMatch == null)
                return;

            if (rankedMatch.EchoMatchInstance == null)
            {
                matchState.TryRemove(rankedMatch.MatchId, out _);
                return;
            }

            lifecycle.StopMatchMonitoring(rankedMatch.MatchId);
            logger.LogInformation("Queue channel deleted, match {MatchId} cleaned up", rankedMatch.MatchId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error handling channel deletion for {ChannelId}", textChannel.Id);
            await discord.LogErrorAsync($"Error handling channel deletion: {ex.Message}");
        }
    }
}
