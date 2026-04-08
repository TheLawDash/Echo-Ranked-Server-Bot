using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using EchoRankedServerBot.Commands;
using EchoRankedServerBot.Configuration;
using EchoRankedServerBot.Data;
using EchoRankedServerBot.Extensions;
using EchoRankedServerBot.Handlers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EchoRankedServerBot.BackgroundServices;

public class DiscordBotService(
    DiscordSocketClient client,
    InteractionService interactions,
    IServiceProvider services,
    IOptions<BotOptions> options,
    ILogger<DiscordBotService> logger)
    : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        // Apply database migrations
        using (var scope = services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
            await db.Database.MigrateAsync(cancellationToken);
            logger.LogInformation("Database migrations applied");
        }

        var token = DataConstants.EnvironmentVariables.EchoRankedDiscordToken.GetAsEnvironmentVariable();

        // Wire events
        client.Log += LogAsync;
        client.Ready += OnReadyAsync;
        client.MessageReceived += OnMessageReceivedAsync;
        client.ChannelCreated += OnChannelCreatedAsync;
        client.ChannelDestroyed += OnChannelDestroyedAsync;
        client.GuildMemberUpdated += OnGuildMemberUpdatedAsync;
        client.SelectMenuExecuted += OnSelectMenuExecutedAsync;
        client.InteractionCreated += OnInteractionCreatedAsync;

        await client.LoginAsync(TokenType.Bot, token);
        await client.StartAsync();

        logger.LogInformation("Discord bot started");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        await client.StopAsync();
        logger.LogInformation("Discord bot stopped");
    }

    private Task OnReadyAsync()
    {
        // Run on a background thread to avoid blocking the gateway task
        _ = Task.Run(async () =>
        {
            try
            {
                await interactions.AddModuleAsync<MatchCommandModule>(services);
                await interactions.RegisterCommandsToGuildAsync(options.Value.GuildId, true);
                logger.LogInformation("Slash commands registered to guild {GuildId}", options.Value.GuildId);

                var handler = services.GetRequiredService<ReadyHandler>();
                await handler.HandleReadyAsync();
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in Ready handler");
            }
        });

        return Task.CompletedTask;
    }

    private async Task OnMessageReceivedAsync(SocketMessage message)
    {
        var handler = services.GetRequiredService<MessageReceivedHandler>();
        await handler.HandleMessageReceivedAsync(message);
    }

    private async Task OnChannelCreatedAsync(SocketChannel channel)
    {
        var handler = services.GetRequiredService<ChannelCreatedHandler>();
        await handler.HandleChannelCreatedAsync(channel);
    }

    private async Task OnChannelDestroyedAsync(SocketChannel channel)
    {
        var handler = services.GetRequiredService<ChannelDestroyedHandler>();
        await handler.HandleChannelDestroyedAsync(channel);
    }

    private async Task OnGuildMemberUpdatedAsync(Cacheable<SocketGuildUser, ulong> before, SocketGuildUser after)
    {
        var handler = services.GetRequiredService<GuildMemberUpdatedHandler>();
        await handler.HandleGuildMemberUpdatedAsync(before, after);
    }

    private async Task OnSelectMenuExecutedAsync(SocketMessageComponent component)
    {
        var handler = services.GetRequiredService<SelectMenuHandler>();
        await handler.HandleSelectMenuExecutedAsync(component);
    }

    private async Task OnInteractionCreatedAsync(SocketInteraction interaction)
    {
        logger.LogInformation("Interaction received: Type={Type}, Id={Id}", interaction.Type, interaction.Id);
        var ctx = new SocketInteractionContext(client, interaction);
        var result = await interactions.ExecuteCommandAsync(ctx, services);
        if (!result.IsSuccess)
            logger.LogError("Interaction failed: {Error} ({ErrorReason})", result.Error, result.ErrorReason);
    }

    private Task LogAsync(LogMessage msg)
    {
        var level = msg.Severity switch
        {
            LogSeverity.Critical => LogLevel.Critical,
            LogSeverity.Error => LogLevel.Error,
            LogSeverity.Warning => LogLevel.Warning,
            LogSeverity.Info => LogLevel.Information,
            LogSeverity.Verbose => LogLevel.Debug,
            LogSeverity.Debug => LogLevel.Trace,
            _ => LogLevel.Information
        };

        logger.Log(level, msg.Exception, "[{Source}] {Message}", msg.Source, msg.Message);
        return Task.CompletedTask;
    }
}
