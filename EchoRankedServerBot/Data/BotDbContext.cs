using EchoRankedServerBot.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace EchoRankedServerBot.Data;

public class BotDbContext : DbContext
{
    public BotDbContext(DbContextOptions<BotDbContext> options) : base(options) { }

    public DbSet<PlayerMatchStat> PlayerMatchStats => Set<PlayerMatchStat>();
    public DbSet<WatchedPlayer> WatchedPlayers => Set<WatchedPlayer>();
    public DbSet<PlayerIdentity> PlayerIdentities => Set<PlayerIdentity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<PlayerMatchStat>(entity =>
        {
            entity.ToTable("player_match_stats");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.DiscordId);
            entity.HasIndex(e => e.MatchName);
            entity.HasIndex(e => e.Timestamp);
        });

        modelBuilder.Entity<WatchedPlayer>(entity =>
        {
            entity.ToTable("watched_players");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.DiscordId);
            entity.HasIndex(e => e.IpAddress);
        });

        modelBuilder.Entity<PlayerIdentity>(entity =>
        {
            entity.ToTable("player_identities");
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.DiscordId);
            entity.HasIndex(e => e.NakamaId);
        });
    }
}
