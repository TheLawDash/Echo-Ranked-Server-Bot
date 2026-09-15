using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using EchoRankedServerBot.Configuration;
using EchoRankedServerBot.Models.Match;
using EchoRankedServerBot.BackgroundServices;
using EchoRankedServerBot.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EchoRankedServerBot.Commands;

public class MatchCommandModule(
    MatchStateService matchState,
    MatchLifecycleService lifecycle,
    MatchMonitorCoordinator monitorCoordinator,
    DiscordChannelService discord,
    WatchService watchService,
    BotConfigService config,
    IOptions<BotOptions> options,
    ILogger<MatchCommandModule> logger)
    : InteractionModuleBase<SocketInteractionContext>
{
    public override Task BeforeExecuteAsync(ICommandInfo command)
    {
        logger.LogInformation("Starting slash command {Name} for user {UserId} in channel {ChannelId}",
            command.Name, Context.User.Id, Context.Channel.Id);
        return Task.CompletedTask;
    }

    public override Task AfterExecuteAsync(ICommandInfo command)
    {
        logger.LogInformation("Finished slash command {Name} for user {UserId}", command.Name, Context.User.Id);
        return Task.CompletedTask;
    }

    [SlashCommand("repull", "Pull a new server for your private match.")]
    // ReSharper disable once UnusedMember.Global
    public async Task RepullServerInstanceAsync()
    {
        if (Context.Guild.Id != options.Value.GuildId)
        {
            logger.LogWarning("Repull command was ignored because guild {GuildId} is not the configured guild {ConfiguredGuildId}",
                Context.Guild.Id, options.Value.GuildId);
            return;
        }

        if (Context.Channel is not SocketTextChannel textChannel || !textChannel.Name.Contains("queue-"))
        {
            logger.LogInformation("Repull command was refused because channel {ChannelId} is not a queue channel", Context.Channel.Id);
            await RespondAsync("Please do this in a proper queue channel.", ephemeral: true);
            return;
        }

        var rankedMatch = matchState.GetByChannelId(textChannel.Id);
        if (rankedMatch == null)
        {
            logger.LogWarning("Could not find a live match for channel {ChannelId}, so the repull command was ignored.", textChannel.Id);
            await RespondAsync("No match found for this channel.", ephemeral: true);
            return;
        }

        // Find NeatQueue message
        var messages = await textChannel.GetMessagesAsync().FlattenAsync(); // Defaults to 100, flatten since it's a readonly enumerable
        var neatQueueMessage = messages.FirstOrDefault(m =>
            m.Embeds.Count > 0 && m.Embeds.Any(e => e.Title != null && e.Title.Contains('⚔')));

        if (neatQueueMessage == null)
        {
            logger.LogWarning("Could not find a NeatQueue message in channel {ChannelId} for match {MatchId}, so the queue has not popped yet.",
                textChannel.Id, rankedMatch.MatchId);
            await RespondAsync("Queue has not yet popped.", ephemeral: true);
            return;
        }

        await RespondAsync("Repulling server now...", ephemeral: true);

        if (rankedMatch.EchoMatchInstance != null)
            lifecycle.StopMatchMonitoring(rankedMatch.MatchId, newInstance: true);

        var embed = neatQueueMessage.Embeds.First();
        var orange = embed.Fields[0].Value.Split(',');
        var blue = embed.Fields[1].Value.Split(',');
        var (teamOrientations, rankedGameMembers) = await lifecycle.SetTeamOrientationsAsync(orange, blue, Context.Guild);

        var hasEu = rankedGameMembers?.Any(x => x.Roles.Any(y => y.Id == options.Value.EuRoleId)) ?? false;
        var matchCreated = await lifecycle.CreateRankedEchoMatchAsync(hasEu, teamOrientations, rankedMatch, rankedGameMembers!);

        if (matchCreated == null)
        {
            logger.LogError("Failed to create a ranked echo match for match {MatchId} in channel {ChannelId}, notifying the channel of the error.",
                rankedMatch.MatchId, textChannel.Id);
            await lifecycle.SendServerPullErrorAsync(textChannel);
            return;
        }

        var ip = matchCreated.Broadcaster.Endpoint.Split(':')[1];
        var location = await lifecycle.GetServerLocationAsync(ip);
        var matchMessageId = await lifecycle.SendServerMessageAsync(
            textChannel, ip, location,
            rankedMatch.PrivateMatchDetails?.MatchMessageId != null,
            rankedMatch.PrivateMatchDetails?.MatchMessageId,
            rankedMatch.PrivateMatchDetails?.DecidedRegion,
            rankedMatch.PrivateMatchDetails?.DecidedAverageLatency,
            rankedMatch.PrivateMatchDetails?.PlayersUsedForDecision);

        var echoMatchId = lifecycle.GetMatchIdFromMatch(matchCreated);

        // Delete old spark link if exists
        if (rankedMatch.PrivateMatchDetails?.SparkLinkMessageId != null)
        {
            try
            {
                var oldSparkMsg = await textChannel.GetMessageAsync(rankedMatch.PrivateMatchDetails.SparkLinkMessageId.Value);
                if (oldSparkMsg != null) await oldSparkMsg.DeleteAsync();
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not delete the old spark link message {MessageId} in channel {ChannelId} for match {MatchId}.",
                    rankedMatch.PrivateMatchDetails.SparkLinkMessageId.Value, textChannel.Id, rankedMatch.MatchId);
            }
        }

        var sparkLinkMsg = await textChannel.SendMessageAsync($"https://echo.taxi/spark://c/{echoMatchId}");

        // Send or update live match message
        var liveMessageId = rankedMatch.PrivateMatchDetails?.LiveMatchMessageId;
        var liveChannel = discord.GetTextChannel(options.Value.LiveMatchesChannelId);
        if (liveChannel == null)
        {
            logger.LogWarning("Could not find the live matches channel {LiveMatchesChannelId}, so the live match message was not updated for match {MatchId}.",
                options.Value.LiveMatchesChannelId, rankedMatch.MatchId);
        }
        else
        {
            var templatePath = Path.Combine(AppContext.BaseDirectory, "Assets", "original.png");
            await using var fileStream = new FileStream(templatePath, FileMode.Open, FileAccess.Read);
            var liveEmbed = new EmbedBuilder()
                .WithColor(Color.Green)
                .WithTitle($"Match for: {textChannel.Name}")
                .AddField("Last updated at:", $"<t:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}:R>")
                .WithImageUrl("attachment://original.png")
                .WithFooter("Echo Ranked • Server Manager");

            if (liveMessageId != null)
            {
                try
                {
                    if (await liveChannel.GetMessageAsync(liveMessageId.Value) is IUserMessage existingMsg)
                    {
                        await existingMsg.ModifyAsync(msg =>
                        {
                            msg.Embed = liveEmbed.Build();
                            msg.Attachments = new[] { new FileAttachment(fileStream, "original.png") };
                        });
                    }
                }
                catch (Exception ex)
                {
                    logger.LogWarning(ex, "Could not modify the existing live match message {MessageId} for match {MatchId}, sending a new one instead.",
                        liveMessageId.Value, rankedMatch.MatchId);
                    var newMsg = await liveChannel.SendFileAsync(fileStream, "original.png", embed: liveEmbed.Build());
                    liveMessageId = newMsg.Id;
                }
            }
            else
            {
                var newMsg = await liveChannel.SendFileAsync(fileStream, "original.png", embed: liveEmbed.Build());
                liveMessageId = newMsg.Id;
            }
        }

        matchState.UpdateMatch(rankedMatch.MatchId, m =>
        {
            m.EchoMatchInstance = new EchoMatchInstance
            {
                StartedTime = DateTime.Now,
                BroadcasterId = matchCreated.Id,
                SessionId = echoMatchId
            };
            m.PrivateMatchDetails!.MatchMessageId = matchMessageId;
            m.PrivateMatchDetails.SparkLinkMessageId = sparkLinkMsg.Id;
            m.PrivateMatchDetails.LiveMatchMessageId = liveMessageId;
            m.PrivateMatchDetails.MatchStarting = false;
        });

        monitorCoordinator.StartMonitoring(rankedMatch.MatchId);
    }

    [SlashCommand("watch", "Watch a user's IP address.")]
    public async Task WatchPlayerAsync(
        [Summary("user", "User to whitelist")] IUser user,
        [Summary("ip", "IP address to watch")] string ipAddress)
    {
        if (Context.Guild.Id != options.Value.GuildId)
        {
            logger.LogWarning("Watch command was ignored because guild {GuildId} is not the configured guild {ConfiguredGuildId}",
                Context.Guild.Id, options.Value.GuildId);
            return;
        }

        if (Context.User.Id != options.Value.OwnerUserId)
        {
            logger.LogWarning("Watch command was refused because user {UserId} is not the bot owner.", Context.User.Id);
            await RespondAsync("This command is reserved for the bot owner.", ephemeral: true);
            return;
        }

        var result = await watchService.WatchAsync(user.Id.ToString(), ipAddress);
        if (!result)
            logger.LogWarning("Failed to watch IP address {IpAddress} for user {UserId}, requested by {RequestingUserId}.",
                ipAddress, user.Id, Context.User.Id);
        else
            logger.LogInformation("Now watching IP address {IpAddress}, excluding user {UserId}, requested by {RequestingUserId}.",
                ipAddress, user.Id, Context.User.Id);

        await RespondAsync(result
            ? $"Successfully watching for users on {ipAddress}, excluding {user.Username}."
            : $"Failed to watch for users on {ipAddress}.",
            ephemeral: true);
    }

    [SlashCommand("unwatch", "Stop watching a user's IP address.")]
    public async Task UnwatchPlayerAsync(
        [Summary("user", "User to unwatch")] IUser user,
        [Summary("ip", "IP address to unwatch")] string ipAddress)
    {
        if (Context.Guild.Id != options.Value.GuildId)
        {
            logger.LogWarning("Unwatch command was ignored because guild {GuildId} is not the configured guild {ConfiguredGuildId}",
                Context.Guild.Id, options.Value.GuildId);
            return;
        }

        if (Context.User.Id != options.Value.OwnerUserId)
        {
            logger.LogWarning("Unwatch command was refused because user {UserId} is not the bot owner.", Context.User.Id);
            await RespondAsync("This command is reserved for the bot owner.", ephemeral: true);
            return;
        }

        var result = await watchService.UnwatchAsync(user.Id.ToString(), ipAddress);
        if (!result)
            logger.LogWarning("Failed to unwatch IP address {IpAddress} for user {UserId}, requested by {RequestingUserId}.",
                ipAddress, user.Id, Context.User.Id);
        else
            logger.LogInformation("Stopped watching IP address {IpAddress}, excluding user {UserId}, requested by {RequestingUserId}.",
                ipAddress, user.Id, Context.User.Id);

        await RespondAsync(result
            ? $"Successfully unwatched users on {ipAddress}, excluding {user.Username}."
            : $"Failed to unwatch users on {ipAddress}.",
            ephemeral: true);
    }

    [SlashCommand("togglemmrrestriction", "Toggle the 1000+ MMR party restriction.")]
    public async Task ToggleMmrRestrictionAsync(
        [Summary("enabled", "Enable or disable")] bool enabled)
    {
        if (Context.Guild.Id != options.Value.GuildId)
        {
            logger.LogWarning("Toggle MMR restriction command was ignored because guild {GuildId} is not the configured guild {ConfiguredGuildId}",
                Context.Guild.Id, options.Value.GuildId);
            return;
        }

        if (Context.User.Id != options.Value.OwnerUserId)
        {
            logger.LogWarning("Toggle MMR restriction command was refused because user {UserId} is not the bot owner.", Context.User.Id);
            await RespondAsync("This command is reserved for the bot owner.", ephemeral: true);
            return;
        }

        config.SetEnforce1000MmrPartyRestriction(enabled);
        logger.LogInformation("1000+ MMR party restriction set to {Enabled} by user {UserId}.", enabled, Context.User.Id);
        var status = enabled ? "**ENABLED**" : "**DISABLED**";
        await RespondAsync($"1000+ MMR party restriction is now {status}.", ephemeral: true);
    }

    [SlashCommand("join", "Manually start monitoring a match via streaming API.")]
    public async Task JoinMatchAsync(
        [Summary("sparkID", "Spark link or session ID")] string sessionId)
    {
        if (Context.Guild.Id != options.Value.GuildId)
        {
            logger.LogWarning("Join command was ignored because guild {GuildId} is not the configured guild {ConfiguredGuildId}",
                Context.Guild.Id, options.Value.GuildId);
            return;
        }

        if (Context.Channel is not SocketTextChannel textChannel || !textChannel.Name.Contains("queue-"))
        {
            logger.LogInformation("Join command was refused because channel {ChannelId} is not a queue channel", Context.Channel.Id);
            await RespondAsync("Please do this in a proper queue channel.", ephemeral: true);
            return;
        }

        // Parse session ID from various formats
        if (sessionId.Contains("taxi") && sessionId.Contains("spark"))
            sessionId = sessionId.Split('/')[6];
        else if (sessionId.Contains("spark"))
            sessionId = sessionId.Split('/')[3];

        await RespondAsync("Starting match monitoring via streaming API!", ephemeral: true);

        var rankedMatch = matchState.GetByChannelId(textChannel.Id);
        if (rankedMatch == null)
        {
            logger.LogWarning("Could not find a live match for channel {ChannelId}, so the join command was ignored.", textChannel.Id);
            return;
        }

        // Send live match message
        ulong? liveMessageId = null;
        var liveChannel = discord.GetTextChannel(options.Value.LiveMatchesChannelId);
        if (liveChannel == null)
        {
            logger.LogWarning("Could not find the live matches channel {LiveMatchesChannelId}, so the live match message was not sent for match {MatchId}.",
                options.Value.LiveMatchesChannelId, rankedMatch.MatchId);
        }
        else
        {
            var templatePath = Path.Combine(AppContext.BaseDirectory, "Assets", "original.png");
            await using var fileStream = new FileStream(templatePath, FileMode.Open, FileAccess.Read);
            var embedBuilder = new EmbedBuilder()
                .WithColor(Color.Green)
                .WithTitle($"Match for: {textChannel.Name}")
                .AddField("Last updated at:", $"<t:{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}:R>")
                .WithImageUrl("attachment://original.png")
                .WithFooter("Echo Ranked • Server Manager");
            var liveMsg = await liveChannel.SendFileAsync(fileStream, "original.png", embed: embedBuilder.Build());
            liveMessageId = liveMsg.Id;
        }

        matchState.UpdateMatch(rankedMatch.MatchId, m =>
        {
            m.EchoMatchInstance = new EchoMatchInstance
            {
                StartedTime = DateTime.Now,
                BroadcasterId = $"{sessionId.ToLower()}.nakama2_us-east",
                SessionId = sessionId
            };
            m.PrivateMatchDetails!.LiveMatchMessageId = liveMessageId;
            m.PrivateMatchDetails.MatchStarting = false;
        });

        monitorCoordinator.StartMonitoring(rankedMatch.MatchId);
    }

    [SlashCommand("manual-pull", "Pull a server on a selected region.")]
    public async Task TestCommandAsync()
    {
        var user = Context.User as SocketGuildUser;
        var isEligible = user?.Roles.Any(role => role.Id == options.Value.AdminRoleId) == true || user?.Id == options.Value.OwnerUserId;
        if (!isEligible)
        {
            logger.LogWarning("Manual pull command was refused because user {UserId} does not have the admin role or owner permission.",
                Context.User.Id);
            await RespondAsync("You do not have permission to use this command.", ephemeral: true);
            return;
        }

        var chicagoRegionCode = GetConfiguredValueOrWarn(options.Value.ChicagoRegionCode, nameof(options.Value.ChicagoRegionCode));
        var dallasRegionCode = GetConfiguredValueOrWarn(options.Value.DallasRegionCode, nameof(options.Value.DallasRegionCode));
        var nebraskaServerIp = GetConfiguredValueOrWarn(options.Value.NebraskaServerIp, nameof(options.Value.NebraskaServerIp));
        var pennsylvaniaServerIp = GetConfiguredValueOrWarn(options.Value.PennsylvaniaServerIp, nameof(options.Value.PennsylvaniaServerIp));
        var kansasServerIp = GetConfiguredValueOrWarn(options.Value.KansasServerIp, nameof(options.Value.KansasServerIp));

        var menuBuilder = new SelectMenuBuilder()
            .WithCustomId("test_server_select")
            .WithPlaceholder("Select a server...")
            .AddOption("Chicago", "chicago", chicagoRegionCode)
            .AddOption("Dallas", "dallas", dallasRegionCode)
            .AddOption("EU", "eu", "EU 180hz")
            .AddOption("Nebraska", "nebraska", nebraskaServerIp)
            .AddOption("Pennsylvania", "pennsylvania", pennsylvaniaServerIp)
            .AddOption("Kansas", "kansas", kansasServerIp);

        var component = new ComponentBuilder().WithSelectMenu(menuBuilder).Build();
        await RespondAsync("Choose a server:", components: component, ephemeral: true);
    }

    private string GetConfiguredValueOrWarn(string? value, string settingName)
    {
        if (!string.IsNullOrEmpty(value))
            return value;

        logger.LogWarning("Configuration setting {SettingName} is empty, using a placeholder description in the manual pull menu.", settingName);
        return "Not configured";
    }
}
