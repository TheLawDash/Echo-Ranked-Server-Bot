using Discord;
using Discord.WebSocket;
using EchoRankedServerBot.Services;

namespace EchoRankedServerBot.Handlers;

public class SelectMenuHandler(
    NakamaApiService nakamaApi,
    MatchLifecycleService lifecycle)
{
    public async Task HandleSelectMenuExecutedAsync(SocketMessageComponent component)
    {
        if (component.Data.CustomId != "test_server_select")
            return;

        var selectedValue = component.Data.Values.First();
        await component.DeferAsync(ephemeral: true);

        await component.ModifyOriginalResponseAsync(msg => msg.Content = "Pulling server, please wait...");

        var token = await nakamaApi.GetNakamaTokenAsync();
        if (token == null)
        {
            await component.ModifyOriginalResponseAsync(msg => msg.Content = "Failed to get Nakama token.");
            return;
        }

        var matches = await nakamaApi.GetNakamaMatchesAsync(token);
        if (matches?.Labels == null || matches.Labels.Count == 0)
        {
            await component.ModifyOriginalResponseAsync(msg => msg.Content = "No matches available.");
            return;
        }

        var selectedMatch = selectedValue switch
        {
            "chicago" => matches.Labels.FirstOrDefault(x =>
                x.Broadcaster.RegionCodes.Contains("redacted-region") && x.LobbyType.Contains("unassigned")),
            "dallas" => matches.Labels.FirstOrDefault(x =>
                x.Broadcaster.RegionCodes.Contains("redacted-region") && x.LobbyType.Contains("unassigned")),
            "eu" => matches.Labels.FirstOrDefault(x =>
                x.Broadcaster.Tags.Contains("180hz") && x.Broadcaster.RegionCodes.Contains("redacted-region") && x.LobbyType.Contains("unassigned")),
            "nebraska" => matches.Labels.FirstOrDefault(x =>
                x.Broadcaster.Endpoint.Contains("0.0.0.0") && x.LobbyType.Contains("unassigned")),
            "pennsylvania" => matches.Labels.FirstOrDefault(x =>
                x.Broadcaster.Endpoint.Contains("0.0.0.0") && x.LobbyType.Contains("unassigned")),
            "kansas" => matches.Labels.FirstOrDefault(x =>
                x.Broadcaster.Endpoint.Contains("0.0.0.0") && x.LobbyType.Contains("unassigned")),
            _ => null
        };

        var containsEu = selectedValue == "eu";
        selectedMatch ??= nakamaApi.GetEmptyEchoMatchAsync(matches, containsEu);

        if (selectedMatch == null)
        {
            await component.ModifyOriginalResponseAsync(msg => msg.Content = "No available server found.");
            return;
        }

        var matchId = await nakamaApi.PrepareEchoMatchAsync(selectedMatch, token, null, component.Channel.Name);
        matchId ??= await nakamaApi.BackupPrepareEchoMatchAsync(selectedMatch, token, null, component.Channel.Name);

        if (matchId == null)
        {
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
}
