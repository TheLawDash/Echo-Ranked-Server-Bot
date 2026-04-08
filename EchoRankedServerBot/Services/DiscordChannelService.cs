using Discord.WebSocket;
using EchoRankedServerBot.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EchoRankedServerBot.Services;

public class DiscordChannelService(
    DiscordSocketClient client,
    IOptions<BotOptions> botOptions,
    ILogger<DiscordChannelService> logger)
{
    /// <summary>
    /// Gets a channel by ID cast as ISocketMessageChannel.
    /// </summary>
    private ISocketMessageChannel? GetChannel(ulong id)
    {
        return client.GetChannel(id) as ISocketMessageChannel;
    }

    /// <summary>
    /// Gets a channel by ID cast as SocketTextChannel.
    /// </summary>
    public SocketTextChannel? GetTextChannel(ulong id)
    {
        return client.GetChannel(id) as SocketTextChannel;
    }

    /// <summary>
    /// Sends an error message to the configured error channel.
    /// </summary>
    public async Task LogErrorAsync(string message)
    {
        try
        {
            var channel = GetChannel(botOptions.Value.ErrorChannelId);
            if (channel is not null)
            {
                await channel.SendMessageAsync(message);
            }
            else
            {
                logger.LogWarning("Error channel {ChannelId} not found. Message: {Message}",
                    botOptions.Value.ErrorChannelId, message);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send error message to error channel");
        }
    }

    /// <summary>
    /// Sends an informational message to the configured log channel.
    /// </summary>
    public async Task LogInfoAsync(string message)
    {
        try
        {
            var channel = GetChannel(botOptions.Value.LogChannelId);
            if (channel is not null)
            {
                await channel.SendMessageAsync(message);
            }
            else
            {
                logger.LogWarning("Log channel {ChannelId} not found. Message: {Message}",
                    botOptions.Value.LogChannelId, message);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send info message to log channel");
        }
    }

    /// <summary>
    /// Returns the current UTC time as a Discord-formatted timestamp string.
    /// </summary>
    public static string GetDiscordTimestamp()
    {
        var epochTime = (int)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
        return $"<t:{epochTime}:f>";
    }
}