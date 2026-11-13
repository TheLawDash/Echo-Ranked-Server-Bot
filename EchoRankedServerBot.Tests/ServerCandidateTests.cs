using EchoRankedServerBot.Configuration;
using EchoRankedServerBot.Models.Nakama;
using EchoRankedServerBot.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace EchoRankedServerBot.Tests;

public class ServerCandidateTests
{
    private static NakamaApiService CreateService(BotOptions? bot = null, string excluded = "") =>
        new(null!,
            Options.Create(new NakamaOptions { ExcludedBroadcasterId = excluded }),
            Options.Create(bot ?? new BotOptions()),
            NullLogger<NakamaApiService>.Instance);

    private static MatchLabel Server(string id, string ip, string lobbyType, params string[] regionCodes) => new()
    {
        Id = id,
        LobbyType = lobbyType,
        Broadcaster = new Broadcaster
        {
            Endpoint = $"172.17.0.2:{ip}:6792",
            RegionCodes = ["0x1234", .. regionCodes],
        },
    };

    // Mirrors the live server list: public servers first, comp servers scattered after them
    private static NakamaMatches LiveLikeMatches() => new()
    {
        Labels =
        [
            Server("newark", "74.99.146.247", "unassigned", "default", "us", "use"),
            Server("london", "82.38.2.140", "unassigned", "euw", "gb"),
            Server("dallas", "47.184.31.187", "unassigned", "echovrce-dal-comp", "us"),
            Server("kci-1", "154.36.247.148", "unassigned", "echovrce-kci-comp", "us"),
            Server("chi-busy", "108.181.62.167", "private", "echovrce-chi-comp", "us"),
            Server("chi-3", "108.181.62.167", "unassigned", "echovrce-chi-comp", "echovrce-chi-comp3", "us"),
            Server("kci-2", "154.36.247.148", "unassigned", "echovrce-kci-comp", "us"),
        ],
    };

    [Fact]
    public void OrdersChicagoThenKansasCityThenDallasThenEverythingElse()
    {
        var ids = CreateService().GetServerCandidates(LiveLikeMatches()).Select(m => m.Id).ToList();

        Assert.Equal(["chi-3", "kci-1", "kci-2", "dallas", "newark", "london"], ids);
    }

    [Fact]
    public void FailsOpenToAnyUnassignedServerWhenNoPreferredRegionIsUp()
    {
        var matches = new NakamaMatches
        {
            Labels =
            [
                Server("busy", "1.1.1.1", "public", "echovrce-chi-comp"),
                Server("public", "2.2.2.2", "unassigned", "default"),
            ],
        };

        var ids = CreateService().GetServerCandidates(matches).Select(m => m.Id).ToList();

        Assert.Equal(["public"], ids);
    }

    [Fact]
    public void ConfiguredPrefixesReplaceTheDefaultOrder()
    {
        var bot = new BotOptions { PreferredRegionCodePrefixes = ["echovrce-dal-comp", "echovrce-chi-comp"] };

        var ids = CreateService(bot).GetServerCandidates(LiveLikeMatches()).Select(m => m.Id).ToList();

        // Kansas City is no longer preferred, so it falls to the fail-open tail in list order
        Assert.Equal(["dallas", "chi-3", "newark", "london", "kci-1", "kci-2"], ids);
    }

    [Fact]
    public void SkipsTheExcludedBroadcaster()
    {
        var ids = CreateService(excluded: "chi-3").GetServerCandidates(LiveLikeMatches()).Select(m => m.Id).ToList();

        Assert.DoesNotContain("chi-3", ids);
        Assert.Equal("kci-1", ids[0]);
    }
}
