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
        if (string.IsNullOrEmpty(discordId) || string.IsNullOrEmpty(ipAddress))
        {
            logger.LogWarning(
                "Skipped adding a watch because the Discord id {DiscordId} or IP address {IpAddress} was empty.",
                discordId, ipAddress);
            return false;
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BotDbContext>();

        try
        {
            var existing = await db.WatchedPlayers
                .FirstOrDefaultAsync(w => w.DiscordId == discordId && w.IpAddress == ipAddress);

            if (existing != null)
            {
                logger.LogWarning(
                    "Skipped adding a watch for {DiscordId} on {IpAddress} because it already exists.",
                    discordId, ipAddress);
                return true; // already watching
            }

            db.WatchedPlayers.Add(new WatchedPlayer
            {
                DiscordId = discordId,
                IpAddress = ipAddress
            });
            await db.SaveChangesAsync();
            logger.LogInformation("Watch added: {DiscordId} on {IpAddress}", discordId, ipAddress);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Could not save the watched player {DiscordId} at {IpAddress} to the database, so the watch was not added.",
                discordId, ipAddress);
            return false;
        }
    }

    public async Task<bool> UnwatchAsync(string discordId, string ipAddress)
    {
        if (string.IsNullOrEmpty(discordId) || string.IsNullOrEmpty(ipAddress))
        {
            logger.LogWarning(
                "Skipped removing a watch because the Discord id {DiscordId} or IP address {IpAddress} was empty.",
                discordId, ipAddress);
            return false;
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BotDbContext>();

        try
        {
            var entry = await db.WatchedPlayers
                .FirstOrDefaultAsync(w => w.DiscordId == discordId && w.IpAddress == ipAddress);

            if (entry == null)
            {
                logger.LogWarning(
                    "Skipped removing a watch for {DiscordId} on {IpAddress} because no matching watch was found.",
                    discordId, ipAddress);
                return false;
            }

            db.WatchedPlayers.Remove(entry);
            await db.SaveChangesAsync();
            logger.LogInformation("Watch removed: {DiscordId} on {IpAddress}", discordId, ipAddress);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Could not remove the watched player {DiscordId} at {IpAddress} from the database, so the watch was not removed.",
                discordId, ipAddress);
            return false;
        }
    }

    /// <summary>
    /// Returns true if the IP is associated with a DIFFERENT Discord ID (detection).
    /// </summary>
    public async Task<bool> CheckWatchAsync(string discordId, string ipAddress)
    {
        if (string.IsNullOrEmpty(discordId) || string.IsNullOrEmpty(ipAddress))
        {
            logger.LogWarning(
                "Skipped checking watched players because the Discord id {DiscordId} or IP address {IpAddress} was empty.",
                discordId, ipAddress);
            return false;
        }

        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BotDbContext>();

        try
        {
            return await db.WatchedPlayers
                .AnyAsync(w => w.IpAddress == ipAddress && w.DiscordId != discordId);
        }
        catch (Exception ex)
        {
            logger.LogError(ex,
                "Could not check watched players for {DiscordId} at {IpAddress}, so no match was reported.",
                discordId, ipAddress);
            return false;
        }
    }
}
