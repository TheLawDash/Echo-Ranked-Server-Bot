using Discord.WebSocket;

namespace EchoRankedServerBot.Services;

public class DiscordChannelService(DiscordSocketClient client)
{
    /// <summary>
    /// Gets a channel by ID cast as SocketTextChannel.
    /// Operational diagnostics are written through ILogger, not to Discord channels,
    /// so this service only resolves channels the bot needs to post user-facing messages to.
    /// </summary>
    public SocketTextChannel? GetTextChannel(ulong id)
    {
        return client.GetChannel(id) as SocketTextChannel;
    }
}
