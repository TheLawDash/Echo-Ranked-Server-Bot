using Discord;
using Discord.WebSocket;
using EchoRankedServerBot.Configuration;
using EchoRankedServerBot.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EchoRankedServerBot.Handlers;

public class SelectMenuHandler(
    NakamaApiService nakamaApi,
    MatchLifecycleService lifecycle,
    IOptions<BotOptions> options,
    ILogger<SelectMenuHandler> logger)
{
    public async Task HandleSelectMenuExecutedAsync(SocketMessageComponent component)
    {
        if (component.Data.CustomId != "test_server_select")
            return;

        var selectedValue = component.Data.Values.First();
        logger.LogInformation(
            "Select menu selection received: CustomId={CustomId}, SelectedValue={SelectedValue}, UserId={UserId}, ChannelId={ChannelId}",
            component.Data.CustomId, selectedValue, component.User?.Id, component.Channel?.Id);

        await component.DeferAsync(ephemeral: true);

        try
        {
            await component.ModifyOriginalResponseAsync(msg => msg.Content = "Pulling server, please wait...");

            var token = await nakamaApi.GetNakamaTokenAsync();
            if (token == null)
            {
                logger.LogError("Could not get a Nakama token while handling selection {SelectedValue} in channel {ChannelId}.",
                    selectedValue, component.Channel?.Id);
                await component.ModifyOriginalResponseAsync(msg => msg.Content = "Failed to get Nakama token.");
                return;
            }

            var matches = await nakamaApi.GetNakamaMatchesAsync(token);
            if (matches?.Labels == null || matches.Labels.Count == 0)
            {
                logger.LogWarning("No Nakama matches were available while handling selection {SelectedValue} in channel {ChannelId}.",
                    selectedValue, component.Channel?.Id);
                await component.ModifyOriginalResponseAsync(msg => msg.Content = "No matches available.");
                return;
            }

            var selectedMatch = selectedValue switch
            {
                "chicago" => matches.Labels.FirstOrDefault(x =>
                    x.Broadcaster.RegionCodes.Contains(options.Value.ChicagoRegionCode) && x.LobbyType.Contains("unassigned")),
                "dallas" => matches.Labels.FirstOrDefault(x =>
                    x.Broadcaster.RegionCodes.Contains(options.Value.DallasRegionCode) && x.LobbyType.Contains("unassigned")),
                "eu" => matches.Labels.FirstOrDefault(x =>
                    x.Broadcaster.Tags.Contains("180hz") && x.Broadcaster.RegionCodes.Contains(options.Value.ChicagoRegionCode) && x.LobbyType.Contains("unassigned")),
                "nebraska" => matches.Labels.FirstOrDefault(x =>
                    x.Broadcaster.Endpoint.Contains(options.Value.NebraskaServerIp) && x.LobbyType.Contains("unassigned")),
                "pennsylvania" => matches.Labels.FirstOrDefault(x =>
                    x.Broadcaster.Endpoint.Contains(options.Value.PennsylvaniaServerIp) && x.LobbyType.Contains("unassigned")),
                "kansas" => matches.Labels.FirstOrDefault(x =>
                    x.Broadcaster.Endpoint.Contains(options.Value.KansasServerIp) && x.LobbyType.Contains("unassigned")),
                _ => null
            };

            var containsEu = selectedValue == "eu";
            selectedMatch ??= nakamaApi.GetEmptyEchoMatchAsync(matches, containsEu);

            if (selectedMatch == null)
            {
                logger.LogWarning("No available server was found for selection {SelectedValue} in channel {ChannelId}.",
                    selectedValue, component.Channel?.Id);
                await component.ModifyOriginalResponseAsync(msg => msg.Content = "No available server found.");
                return;
            }

            var channelName = component.Channel?.Name;
            if (channelName is null)
            {
                logger.LogWarning(
                    "The channel for selection {SelectedValue} could not be resolved, using a generic queue name to prepare the server.",
                    selectedValue);
                channelName = "unknown-queue";
            }

            var matchId = await nakamaApi.PrepareEchoMatchAsync(selectedMatch, token, null, channelName);
            matchId ??= await nakamaApi.BackupPrepareEchoMatchAsync(selectedMatch, token, null, channelName);

            if (matchId == null)
            {
                logger.LogError("Failed to prepare the server for selection {SelectedValue} in channel {ChannelId}.",
                    selectedValue, component.Channel?.Id);
                await component.ModifyOriginalResponseAsync(msg => msg.Content = "Failed to prepare the server.");
                return;
            }

            var ip = selectedMatch.Broadcaster.Endpoint.Split(':')[1];
            var location = await lifecycle.GetServerLocationAsync(ip);
            var regionLabel = ServerDecisionService.GetRegionLabel(selectedMatch.Broadcaster.RegionCodes);
            var echoMatchId = lifecycle.GetMatchIdFromMatch(selectedMatch);

            var embed = new EmbedBuilder()
                .WithColor(Color.Green)
                .WithTitle("Server has been reserved!")
                .WithDescription(
                    $"Config: Nakama Global Config\n\n" +
                    $"Server IP: {ip}\n\n" +
                    $"Server Location: {location}\n\n" +
                    $"Selected Region: {regionLabel}\n\n" +
                    $"Please open echo, and click \"Play\" or go to a matchmaking terminal and hit \"Find Match\" to join!\n\n" +
                    $"Your session will be held until: `{DateTime.Now.AddMinutes(5):hh:mm tt} EST`\n\n")
                .WithThumbnailUrl("https://cdn.discordapp.com/attachments/1230261297287794950/1230563467606360064/EchoRanked.png")
                .WithFooter($"Today at {DateTime.Now.AddMinutes(5):hh:mm tt}")
                .Build();

            if (component.Channel is SocketTextChannel textChannel)
            {
                await textChannel.SendMessageAsync(embed: embed);
                await textChannel.SendMessageAsync($"https://echo.taxi/spark://c/{echoMatchId}");
            }

            await component.ModifyOriginalResponseAsync(msg => msg.Content = "Server reserved.");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error while handling select menu selection {SelectedValue} in channel {ChannelId} for user {UserId}.",
                selectedValue, component.Channel?.Id, component.User?.Id);

            try
            {
                await component.ModifyOriginalResponseAsync(msg =>
                    msg.Content = "Something went wrong while pulling the server. The error has been logged.");
            }
            catch (Exception modifyEx)
            {
                logger.LogError(modifyEx, "Failed to notify the user about the error for selection {SelectedValue} in channel {ChannelId}.",
                    selectedValue, component.Channel?.Id);
            }
        }
    }
}
