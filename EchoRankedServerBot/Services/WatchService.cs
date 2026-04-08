using EchoRankedServerBot.Data;
using EchoRankedServerBot.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EchoRankedServerBot.Services;

public class WatchService(IServiceScopeFactory scopeFactory, ILogger<WatchService> logger)
{
    public async Task<bool> WatchAsync(string discordId, string ipAddress)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BotDbContext>();

        var existing = await db.WatchedPlayers
            .FirstOrDefaultAsync(w => w.DiscordId == discordId && w.IpAddress == ipAddress);

        if (existing != null)
            return true; // already watching

        db.WatchedPlayers.Add(new WatchedPlayer
        {
            DiscordId = discordId,
            IpAddress = ipAddress
        });
        await db.SaveChangesAsync();
        logger.LogInformation("Watch added: {DiscordId} on {IpAddress}", discordId, ipAddress);
        return true;
    }

    public async Task<bool> UnwatchAsync(string discordId, string ipAddress)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BotDbContext>();

        var entry = await db.WatchedPlayers
            .FirstOrDefaultAsync(w => w.DiscordId == discordId && w.IpAddress == ipAddress);

        if (entry == null)
            return false;

        db.WatchedPlayers.Remove(entry);
        await db.SaveChangesAsync();
        logger.LogInformation("Watch removed: {DiscordId} on {IpAddress}", discordId, ipAddress);
        return true;
    }

    /// <summary>
    /// Returns true if the IP is associated with a DIFFERENT Discord ID (detection).
    /// </summary>
    public async Task<bool> CheckWatchAsync(string discordId, string ipAddress)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BotDbContext>();

        return await db.WatchedPlayers
            .AnyAsync(w => w.IpAddress == ipAddress && w.DiscordId != discordId);
    }
}
