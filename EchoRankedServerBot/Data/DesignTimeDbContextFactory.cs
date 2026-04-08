using EchoRankedServerBot.Configuration;
using EchoRankedServerBot.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EchoRankedServerBot.Data;

public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<BotDbContext>
{
    public BotDbContext CreateDbContext(string[] args)
    {
        var connectionString = DataConstants.EnvironmentVariables.EchoRankedPostgresConnection.GetAsEnvironmentVariable();

        var optionsBuilder = new DbContextOptionsBuilder<BotDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new BotDbContext(optionsBuilder.Options);
    }
}
