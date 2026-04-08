using Discord;
using Discord.Interactions;
using Discord.WebSocket;
using EchoRankedServerBot.Configuration;
using EchoRankedServerBot.Models.Match;
using EchoRankedServerBot.BackgroundServices;
using EchoRankedServerBot.Services;
using Microsoft.Extensions.Options;

namespace EchoRankedServerBot.Commands;

public class MatchCommandModule(
    MatchStateService matchState,
    MatchLifecycleService lifecycle,
    MatchMonitorCoordinator monitorCoordinator,
    DiscordChannelService discord,
    WatchService watchService,
    BotConfigService config,
    IOptions<BotOptions> options)
    : InteractionModuleBase<SocketInteractionContext>
{
    [SlashCommand("repull", "Pull a new server for your private match.")]
    // ReSharper disable once UnusedMember.Global
    public async Task RepullServerInstanceAsync()
    {
        if (Context.Guild.Id != options.Value.GuildId)
            return;

        if (Context.Channel is not SocketTextChannel textChannel || !textChannel.Name.Contains("queue-"))
        {
            await RespondAsync("Please do this in a proper queue channel.", ephemeral: true);
            return;
        }

        var rankedMatch = matchState.GetByChannelId(textChannel.Id);
        if (rankedMatch == null)
        {
            await RespondAsync("No match found for this channel.", ephemeral: true);
            return;
        }

        // Find NeatQueue message
        var messages = await textChannel.GetMessagesAsync().FlattenAsync(); // Defaults to 100, flatten since it's a readonly enumerable
        var neatQueueMessage = messages.FirstOrDefault(m =>
            m.Embeds.Count > 0 && m.Embeds.Any(e => e.Title != null && e.Title.Contains('⚔')));

        if (neatQueueMessage == null)
        {
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
            catch
            {
                // ignored
            }
        }

        var sparkLinkMsg = await textChannel.SendMessageAsync($"https://echo.taxi/spark://c/{echoMatchId}");

        // Send or update live match message
        var liveMessageId = rankedMatch.PrivateMatchDetails?.LiveMatchMessageId;
        var liveChannel = discord.GetTextChannel(options.Value.LiveMatchesChannelId);
        if (liveChannel != null)
        {
            var templatePath = Path.Combine(AppContext.BaseDirectory, "Assets", "original.png");
            await using var fileStream = new FileStream(templatePath, FileMode.Open, FileAccess.Read);
            var liveEmbed = new EmbedBuilder()
                .WithColor(Color.Green)
                .WithTitle($"Match for: {textChannel.Name}")
                .WithImageUrl("attachment://original.png")
                .WithFooter($"Updated at {DateTime.Now:hh:mm tt}");

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
                catch
                {
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
        if (Context.Guild.Id != options.Value.GuildId) return;
        if (Context.User.Id != options.Value.OwnerUserId)
        {
            await RespondAsync("This command is reserved for the bot owner.", ephemeral: true);
            return;
        }

        var result = await watchService.WatchAsync(user.Id.ToString(), ipAddress);
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
        if (Context.Guild.Id != options.Value.GuildId) return;
        if (Context.User.Id != options.Value.OwnerUserId)
        {
            await RespondAsync("This command is reserved for the bot owner.", ephemeral: true);
            return;
        }

        var result = await watchService.UnwatchAsync(user.Id.ToString(), ipAddress);
        await RespondAsync(result
            ? $"Successfully unwatched users on {ipAddress}, excluding {user.Username}."
            : $"Failed to unwatch users on {ipAddress}.",
            ephemeral: true);
    }

    [SlashCommand("togglemmrrestriction", "Toggle the 1000+ MMR party restriction.")]
    public async Task ToggleMmrRestrictionAsync(
        [Summary("enabled", "Enable or disable")] bool enabled)
    {
        if (Context.Guild.Id != options.Value.GuildId) return;
        if (Context.User.Id != options.Value.OwnerUserId)
        {
            await RespondAsync("This command is reserved for the bot owner.", ephemeral: true);
            return;
        }

        config.SetEnforce1000MmrPartyRestriction(enabled);
        var status = enabled ? "**ENABLED**" : "**DISABLED**";
        await RespondAsync($"1000+ MMR party restriction is now {status}.", ephemeral: true);
    }

    [SlashCommand("join", "Manually start monitoring a match via streaming API.")]
    public async Task JoinMatchAsync(
        [Summary("sparkID", "Spark link or session ID")] string sessionId)
    {
        if (Context.Guild.Id != options.Value.GuildId) return;

        if (Context.Channel is not SocketTextChannel textChannel || !textChannel.Name.Contains("queue-"))
        {
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
        if (rankedMatch == null) return;

        // Send live match message
        ulong? liveMessageId = null;
        var liveChannel = discord.GetTextChannel(options.Value.LiveMatchesChannelId);
        if (liveChannel != null)
        {
            var templatePath = Path.Combine(AppContext.BaseDirectory, "Assets", "original.png");
            await using var fileStream = new FileStream(templatePath, FileMode.Open, FileAccess.Read);
            var embedBuilder = new EmbedBuilder()
                .WithColor(Color.Green)
                .WithTitle($"Match for: {textChannel.Name}")
                .WithImageUrl("attachment://original.png")
                .WithFooter($"Updated at {DateTime.Now:hh:mm tt}");
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
            await RespondAsync("You do not have permission to use this command.", ephemeral: true);
            return;
        }

        var menuBuilder = new SelectMenuBuilder()
            .WithCustomId("test_server_select")
            .WithPlaceholder("Select a server...")
            .AddOption("Chicago", "chicago", "redacted-region")
            .AddOption("Dallas", "dallas", "redacted-region")
            .AddOption("EU", "eu", "EU 180hz")
            .AddOption("Nebraska", "nebraska", "0.0.0.0")
            .AddOption("Pennsylvania", "pennsylvania", "0.0.0.0")
            .AddOption("Kansas", "kansas", "0.0.0.0");

        var component = new ComponentBuilder().WithSelectMenu(menuBuilder).Build();
        await RespondAsync("Choose a server:", components: component, ephemeral: true);
    }
}
