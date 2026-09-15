using System.Diagnostics;
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
    private bool _modulesRegistered;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            // Apply database migrations
            using (var scope = services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BotDbContext>();
                await db.Database.MigrateAsync(cancellationToken);
                logger.LogInformation("Database migrations applied");
            }
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Failed to apply database migrations, the bot cannot start");
            throw;
        }

        var token = DataConstants.EnvironmentVariables.EchoRankedDiscordToken.GetAsEnvironmentVariable();

        // Wire events
        client.Log += LogAsync;
        interactions.Log += LogAsync;
        client.Ready += OnReadyAsync;
        client.Connected += OnConnectedAsync;
        client.Disconnected += OnDisconnectedAsync;
        client.MessageReceived += OnMessageReceivedAsync;
        client.ChannelCreated += OnChannelCreatedAsync;
        client.ChannelDestroyed += OnChannelDestroyedAsync;
        client.GuildMemberUpdated += OnGuildMemberUpdatedAsync;
        client.SelectMenuExecuted += OnSelectMenuExecutedAsync;
        client.InteractionCreated += OnInteractionCreatedAsync;
        interactions.SlashCommandExecuted += OnSlashCommandExecutedAsync;
        interactions.ComponentCommandExecuted += OnComponentCommandExecutedAsync;

        try
        {
            await client.LoginAsync(TokenType.Bot, token);
            await client.StartAsync();
        }
        catch (Exception ex)
        {
            logger.LogCritical(ex, "Failed to log in or start the Discord client, the bot cannot start");
            throw;
        }

        logger.LogInformation("Discord bot started");
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        try
        {
            await client.StopAsync();
            logger.LogInformation("Discord bot stopped");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error while stopping the Discord client");
        }
    }

    private Task OnConnectedAsync()
    {
        logger.LogInformation("Discord client connected to the gateway");
        return Task.CompletedTask;
    }

    private Task OnDisconnectedAsync(Exception ex)
    {
        logger.LogWarning(ex, "Discord client disconnected from the gateway");
        return Task.CompletedTask;
    }

    private Task OnReadyAsync()
    {
        if (_modulesRegistered)
        {
            logger.LogInformation("Ready event fired again, modules are already registered so registration was skipped");
            return Task.CompletedTask;
        }

        _modulesRegistered = true;

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
                logger.LogError(ex, "Error in Ready handler while registering modules for guild {GuildId}", options.Value.GuildId);
            }
        });

        return Task.CompletedTask;
    }

    private Task OnMessageReceivedAsync(SocketMessage message)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var handler = services.GetRequiredService<MessageReceivedHandler>();
                await handler.HandleMessageReceivedAsync(message);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in MessageReceived handler for message {MessageId} from user {UserId} in channel {ChannelId}",
                    message.Id, message.Author?.Id, message.Channel?.Id);
            }
        });
        return Task.CompletedTask;
    }

    private Task OnChannelCreatedAsync(SocketChannel channel)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var handler = services.GetRequiredService<ChannelCreatedHandler>();
                await handler.HandleChannelCreatedAsync(channel);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in ChannelCreated handler for channel {ChannelId} ({ChannelName})",
                    channel.Id, (channel as SocketGuildChannel)?.Name);
            }
        });
        return Task.CompletedTask;
    }

    private Task OnChannelDestroyedAsync(SocketChannel channel)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var handler = services.GetRequiredService<ChannelDestroyedHandler>();
                await handler.HandleChannelDestroyedAsync(channel);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in ChannelDestroyed handler for channel {ChannelId} ({ChannelName})",
                    channel.Id, (channel as SocketGuildChannel)?.Name);
            }
        });
        return Task.CompletedTask;
    }

    private Task OnGuildMemberUpdatedAsync(Cacheable<SocketGuildUser, ulong> before, SocketGuildUser after)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var handler = services.GetRequiredService<GuildMemberUpdatedHandler>();
                await handler.HandleGuildMemberUpdatedAsync(before, after);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in GuildMemberUpdated handler for user {UserId} in guild {GuildId}",
                    after.Id, after.Guild?.Id);
            }
        });
        return Task.CompletedTask;
    }

    private Task OnSelectMenuExecutedAsync(SocketMessageComponent component)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                var handler = services.GetRequiredService<SelectMenuHandler>();
                await handler.HandleSelectMenuExecutedAsync(component);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in SelectMenuExecuted handler for message {MessageId} from user {UserId} in channel {ChannelId}",
                    component.Message?.Id, component.User?.Id, component.Channel?.Id);
            }
        });
        return Task.CompletedTask;
    }

    private Task OnInteractionCreatedAsync(SocketInteraction interaction)
    {
        _ = Task.Run(async () =>
        {
            var commandName = interaction is SocketSlashCommand slashCommand ? slashCommand.Data.Name : null;

            try
            {
                logger.LogInformation(
                    "Interaction received: Type={Type}, Id={Id}, UserId={UserId}, CommandName={CommandName}",
                    interaction.Type, interaction.Id, interaction.User?.Id, commandName);

                var ctx = new SocketInteractionContext(client, interaction);
                var stopwatch = Stopwatch.StartNew();
                var executeTask = interactions.ExecuteCommandAsync(ctx, services);

                _ = WatchForSlowInteractionAsync(executeTask, interaction.Id, commandName);

                var result = await executeTask;
                stopwatch.Stop();

                logger.LogInformation(
                    "Interaction {Id} ({CommandName}) finished in {ElapsedMilliseconds} ms",
                    interaction.Id, commandName, stopwatch.ElapsedMilliseconds);

                if (!result.IsSuccess)
                    logger.LogError("Interaction {Id} ({CommandName}) failed: {Error} ({ErrorReason})",
                        interaction.Id, commandName, result.Error, result.ErrorReason);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error in InteractionCreated handler for interaction {Id} ({CommandName})",
                    interaction.Id, commandName);
            }
        });
        return Task.CompletedTask;
    }

    private async Task WatchForSlowInteractionAsync(Task executeTask, ulong interactionId, string? commandName)
    {
        var firstWarning = await Task.WhenAny(executeTask, Task.Delay(TimeSpan.FromSeconds(3)));
        if (firstWarning == executeTask)
            return;

        logger.LogWarning(
            "Interaction {Id} ({CommandName}) has not finished after 3 seconds; Discord will report that the application did not respond",
            interactionId, commandName);

        var secondWarning = await Task.WhenAny(executeTask, Task.Delay(TimeSpan.FromSeconds(27)));
        if (secondWarning == executeTask)
            return;

        logger.LogWarning(
            "Interaction {Id} ({CommandName}) has not finished after 30 seconds",
            interactionId, commandName);
    }

    private Task OnSlashCommandExecutedAsync(SlashCommandInfo commandInfo, IInteractionContext context, IResult result)
    {
        var userId = context.User?.Id;
        var guildId = context.Guild?.Id;
        var channelId = context.Channel?.Id;

        if (result.IsSuccess)
        {
            logger.LogInformation("Slash command {CommandName} completed for user {UserId}",
                commandInfo.Name, userId);
        }
        else
        {
            logger.LogError(
                "Slash command {CommandName} failed for user {UserId} in guild {GuildId} channel {ChannelId}: {Error} ({ErrorReason})",
                commandInfo.Name, userId, guildId, channelId, result.Error, result.ErrorReason);
        }

        return Task.CompletedTask;
    }

    private Task OnComponentCommandExecutedAsync(ComponentCommandInfo commandInfo, IInteractionContext context, IResult result)
    {
        var userId = context.User?.Id;
        var guildId = context.Guild?.Id;
        var channelId = context.Channel?.Id;

        if (result.IsSuccess)
        {
            logger.LogInformation("Component command {CommandName} completed for user {UserId}",
                commandInfo.Name, userId);
        }
        else
        {
            logger.LogError(
                "Component command {CommandName} failed for user {UserId} in guild {GuildId} channel {ChannelId}: {Error} ({ErrorReason})",
                commandInfo.Name, userId, guildId, channelId, result.Error, result.ErrorReason);
        }

        return Task.CompletedTask;
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
