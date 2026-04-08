using EchoRankedServerBot.Data;
using EchoRankedServerBot.Data.Entities;
using EchoRankedServerBot.Models.Stats;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace EchoRankedServerBot.Services;

public class StatsRepository(IServiceScopeFactory scopeFactory, ILogger<StatsRepository> logger)
{
    public async Task SaveMatchStatsAsync(PostStatsRequest stats)
    {
        using var scope = scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<BotDbContext>();

        var entity = new PlayerMatchStat
        {
            PlayerName = stats.PlayerName,
            PlayerId = stats.PlayerId,
            DiscordUsername = stats.DiscordUsername,
            DiscordId = stats.DiscordId,
            EvrId = stats.EvrId,
            UserId = stats.UserId,
            UserIp = stats.UserIp,
            MatchName = stats.PrivateName,
            Timestamp = DateTimeOffset.UtcNow,
            Win = stats.Win,
            Lose = stats.Lose,
            Mvp = stats.Mvp,
            MvpScore = stats.MvpScore,
            Points = stats.Points,
            Saves = stats.Saves,
            Assists = stats.Assists,
            PossessionTime = stats.PossessionTime,
            Stuns = stats.Stuns,
            Passes = stats.Passes,
            Catches = stats.Catches,
            Steals = stats.Steals,
            Blocks = stats.Blocks,
            Interceptions = stats.Interceptions,
            Goals = stats.Goals,
            ShotsTaken = stats.ShotsTaken,
            LongBounceShots = stats.LongBounceShots,
            ThreePointShots = stats.ThreePointShots,
            TwoPointShots = stats.TwoPointShots,
            ShortBounceShots = stats.ShortBounceShots,
            ThrowDistances = stats.ThrowDistance,
            ShotSpeeds = stats.ShotSpeed
        };

        db.PlayerMatchStats.Add(entity);
        await db.SaveChangesAsync();
        logger.LogInformation("Stats saved for {PlayerName} in {MatchName}", stats.PlayerName, stats.PrivateName);
    }
}
