using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using EchoRankedServerBot.BackgroundServices;
using EchoRankedServerBot.Configuration;
using EchoRankedServerBot.Data;
using EchoRankedServerBot.Handlers;
using EchoRankedServerBot.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace EchoRankedServerBot.Extensions;

public static class ServiceCollectionExtensions
{
    public static void AddEchoRankedBot(this IServiceCollection services, IConfiguration configuration)
    {
        // Configuration
        services.Configure<BotOptions>(configuration.GetSection(BotOptions.SectionName));
        services.Configure<NakamaOptions>(o =>
        {
            configuration.GetSection(NakamaOptions.SectionName).Bind(o);
            o.Username = DataConstants.EnvironmentVariables.EchoRankedNakamaUsername.GetAsEnvironmentVariable();
            o.Password = DataConstants.EnvironmentVariables.EchoRankedNakamaPassword.GetAsEnvironmentVariable();
            o.HttpKey = DataConstants.EnvironmentVariables.EchoRankedNakamaHttpKey.GetAsEnvironmentVariable();
        });
        services.Configure<ApiOptions>(configuration.GetSection(ApiOptions.SectionName));

        // Database
        var connectionString = DataConstants.EnvironmentVariables.EchoRankedPostgresConnection.GetAsEnvironmentVariable();
        services.AddDbContext<BotDbContext>(options =>
            options.UseNpgsql(connectionString));

        // Discord
        var clientConfig = new DiscordSocketConfig
        {
            GatewayIntents = GatewayIntents.All,
            LogLevel = LogSeverity.Info,
            AlwaysDownloadUsers = true,
            MessageCacheSize = 100,
            // The default RetryMode is AlwaysRetry, which silently retries a timed out REST
            // request forever with no log line, so a hung request never surfaces as a failure.
            // RetryRatelimit only retries on a rate limit and lets other failures throw.
            DefaultRetryMode = RetryMode.RetryRatelimit
        };
        var client = new DiscordSocketClient(clientConfig);
        services.AddSingleton(client);

        var interactionServiceConfig = new InteractionServiceConfig
        {
            LogLevel = LogSeverity.Info,
            // Errors must surface through the returned IResult instead of being swallowed by a
            // fire and forget async run mode.
            DefaultRunMode = RunMode.Sync,
            ThrowOnError = true
        };
        services.AddSingleton(new InteractionService(client, interactionServiceConfig));

        // HTTP clients
        services.AddHttpClient("Nakama");
        services.AddHttpClient("IpApi");
        services.AddHttpClient("NeatQueue", (_, httpClient) =>
        {
            var neatQueueApiKey = DataConstants.EnvironmentVariables.EchoRankedNeatQueueApiKey.GetAsEnvironmentVariable();
            if (!string.IsNullOrEmpty(neatQueueApiKey))
                httpClient.DefaultRequestHeaders.Add("Authorization", neatQueueApiKey);
        });

        // Services (singletons)
        services.AddSingleton<MatchStateService>();
        services.AddSingleton<BotConfigService>();
        services.AddSingleton<DiscordChannelService>();

        // Services (scoped/transient)
        services.AddSingleton<NakamaApiService>();
        services.AddSingleton<StreamingApiService>();
        services.AddSingleton<IpGeolocationService>();
        services.AddSingleton<NeatQueueService>();
        services.AddSingleton<ServerDecisionService>();
        services.AddSingleton<ScoreboardImageService>();
        services.AddSingleton<MatchLifecycleService>();
        services.AddSingleton<StatsRepository>();
        services.AddSingleton<WatchService>();
        services.AddSingleton<MatchMonitorCoordinator>();

        // Handlers
        services.AddTransient<ReadyHandler>();
        services.AddTransient<MessageReceivedHandler>();
        services.AddTransient<ChannelCreatedHandler>();
        services.AddTransient<ChannelDestroyedHandler>();
        services.AddTransient<GuildMemberUpdatedHandler>();
        services.AddTransient<SelectMenuHandler>();

        // Hosted service
        services.AddHostedService<DiscordBotService>();
    }
}
