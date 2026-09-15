using System.Text.RegularExpressions;
using Discord;
using Discord.WebSocket;
using EchoRankedServerBot.Services;
using Microsoft.Extensions.Logging;

namespace EchoRankedServerBot.Handlers;

public partial class GuildMemberUpdatedHandler(
    BotConfigService config,
    ILogger<GuildMemberUpdatedHandler> logger)
{
    public async Task HandleGuildMemberUpdatedAsync(Cacheable<SocketGuildUser, ulong> before, SocketGuildUser after)
    {
        try
        {
            if (!config.IsEnforce1000MmrPartyRestrictionEnabled())
            {
                logger.LogDebug("Ignoring nickname update for {User} because the 1000 MMR party restriction is disabled", after.Username);
                return;
            }

            var beforeUser = before.HasValue ? before.Value : null;
            if (beforeUser == null)
            {
                logger.LogDebug("Ignoring nickname update for {User} because the cached previous state was not available", after.Username);
                return;
            }

            if (beforeUser.Nickname == after.Nickname)
            {
                logger.LogDebug("Ignoring update for {User} because the nickname did not change", after.Username);
                return;
            }

            var oldMmr = ExtractMmrFromNickname(beforeUser.Nickname);
            var newMmr = ExtractMmrFromNickname(after.Nickname);

            if (!oldMmr.HasValue || !newMmr.HasValue)
            {
                logger.LogDebug("Ignoring nickname update for {User} because an MMR value could not be parsed from old nickname {OldNickname} or new nickname {NewNickname}", after.Username, beforeUser.Nickname, after.Nickname);
                return;
            }

            if (oldMmr < 1000 && newMmr >= 1000)
            {
                logger.LogInformation("User {User} crossed the 1000 MMR threshold, sending party restriction warning", after.Username);
                await SendMmrPartyRestrictionWarningAsync(after);
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error processing nickname change for {User} ({UserId})", after.Username, after.Id);
        }
    }

    private static int? ExtractMmrFromNickname(string? nickname)
    {
        if (string.IsNullOrEmpty(nickname))
            return null;

        var match = MmrRegex().Match(nickname);
        if (match.Success && int.TryParse(match.Groups[1].Value, out var mmr))
            return mmr;

        return null;
    }

    private async Task SendMmrPartyRestrictionWarningAsync(SocketGuildUser member)
    {
        try
        {
            var embed = new EmbedBuilder()
                .WithColor(Color.Red)
                .WithTitle("New Rule - Duo Queue Restrictions")
                .WithDescription("Congratulations on reaching 1000+ MMR! However, you are now subject to new party queue restrictions.")
                .AddField("Important Information",
                    "**Any player with an MMR of 1000 or higher is strictly prohibited from queuing with any other player in a duo or group.**")
                .AddField("What this means for you",
                    "- You may **only solo queue**\n" +
                    "- If a match forms with an illegal party, it may be cancelled\n" +
                    "- If mods don't catch it, the entire game will be reverted\n" +
                    "- **All members** of the party will be punished, even those under 1000 MMR")
                .AddField("Punishment Structure",
                    "**1st offense:** Warning\n" +
                    "**2nd offense:** 1 day ban\n" +
                    "**3rd offense:** 3 day ban\n" +
                    "**4th offense:** 7 day ban\n" +
                    "**5th offense:** 30 day ban")
                .AddField("Our Goal",
                    "We want to balance the queue while still allowing duoing for players under 1000 MMR.")
                .AddField("Read More",
                    "[Click here for the full announcement](https://discord.com/channels/1107535364404551710/1158164771183534160/1437202948756209894)")
                .WithFooter("Echo Arena Ranked | Party Queue Restrictions")
                .WithCurrentTimestamp()
                .Build();

            var dmChannel = await member.CreateDMChannelAsync();
            await dmChannel.SendMessageAsync(embed: embed);

            logger.LogInformation("Sent the 1000 MMR party restriction warning to {User} ({UserId})", member.Username, member.Id);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to send the 1000 MMR party restriction warning to {User} ({UserId})", member.Username, member.Id);
        }
    }

    [GeneratedRegex(@"\((\d+)\)")]
    private static partial Regex MmrRegex();
}
