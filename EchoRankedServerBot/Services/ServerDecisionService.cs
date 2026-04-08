using System.Text;
using EchoRankedServerBot.Models.Match;
using EchoRankedServerBot.Models.Nakama;
using Microsoft.Extensions.Logging;

namespace EchoRankedServerBot.Services;

public class ServerDecisionResult
{
    public required MatchLabel Match { get; set; }
    public required string RegionCode { get; set; }
    public string RegionLabel { get; set; } = "Unknown Region";
    public string ServerIp { get; set; } = string.Empty;
    public double AverageLatency { get; set; }
    public int PlayersUsed { get; set; }
}

public class ServerDecisionService(
    NakamaApiService nakamaApi,
    ILogger<ServerDecisionService> logger)
{
    /// <summary>
    /// Determines the best server for a match based on average player latency data.
    /// </summary>
    public async Task<ServerDecisionResult?> DecideBestServerAsync(
        NakamaMatches matches,
        List<TeamOrientation>? players,
        TokenResponse token)
    {
        if (players is null || players.Count == 0 || matches.Labels.Count == 0)
            return null;

        var playerLatencyTasks = players
            .Where(p => !string.IsNullOrWhiteSpace(p.NakamaId))
            .Select(async player =>
                (player, latency: await nakamaApi.GetUserLatencyDataAsync(player.NakamaId!, token)))
            .ToList();

        var latencyResults = await Task.WhenAll(playerLatencyTasks);

        var playerLatencies = latencyResults
            .Where(r => r.latency?.GameServerLatencies is { Count: > 0 })
            .ToDictionary(
                r => r.player.DiscordId ?? r.player.NakamaId ?? Guid.NewGuid().ToString(),
                r => r.latency!);

        if (playerLatencies.Count == 0)
            return null;

        var candidateMatches = matches.Labels
            .Where(m => m.LobbyType.Contains("unassigned", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var candidates = new List<ServerCandidate>();

        foreach (var match in candidateMatches)
        {
            var endpointParts = match.Broadcaster.Endpoint.Split(':');
            if (endpointParts.Length < 2)
                continue;

            var serverIp = endpointParts[1];
            var latencies = new List<double>();

            foreach (var playerLatency in playerLatencies)
            {
                if (playerLatency.Value.GameServerLatencies.TryGetValue(serverIp, out var pings) && pings.Count > 0)
                {
                    latencies.Add(pings.Average(x => x.LatencyInMs));
                }
            }

            if (latencies.Count == 0)
                continue;

            candidates.Add(new ServerCandidate
            {
                Match = match,
                ServerIp = serverIp,
                PlayersWithData = latencies.Count,
                AverageLatency = latencies.Average(),
                MaxLatency = latencies.Max()
            });
        }

        if (candidates.Count == 0)
            return null;

        var bestServer = candidates
            .OrderByDescending(c => c.PlayersWithData)
            .ThenBy(c => c.AverageLatency)
            .ThenBy(c => c.MaxLatency)
            .First();

        var regionCode = bestServer.Match.Broadcaster.RegionCodes.FirstOrDefault() ?? "unknown";
        var regionLabel = GetRegionLabel(bestServer.Match.Broadcaster.RegionCodes);

        var logBuilder = new StringBuilder();
        logBuilder.AppendLine($"Selected server {bestServer.ServerIp} ({regionLabel}) for this match.");
        logBuilder.AppendLine($"Average latency: {bestServer.AverageLatency:F1}ms");
        logBuilder.AppendLine($"Players used in calculation: {bestServer.PlayersWithData}/{playerLatencies.Count}");
        logger.LogInformation("{ServerDecisionLog}", logBuilder.ToString());

        return new ServerDecisionResult
        {
            Match = bestServer.Match,
            RegionCode = regionCode,
            RegionLabel = regionLabel,
            ServerIp = bestServer.ServerIp,
            AverageLatency = bestServer.AverageLatency,
            PlayersUsed = bestServer.PlayersWithData
        };
    }

    /// <summary>
    /// Maps region codes to human-readable region labels.
    /// </summary>
    public static string GetRegionLabel(List<string>? regionCodes)
    {
        if (regionCodes is null || regionCodes.Count == 0)
            return "Unknown Region";

        if (regionCodes.Any(r => r.Contains("chi", StringComparison.OrdinalIgnoreCase)))
            return "Chicago";

        if (regionCodes.Any(r => r.Contains("dal", StringComparison.OrdinalIgnoreCase)))
            return "Dallas";

        if (regionCodes.Any(r => r.Contains("eu", StringComparison.OrdinalIgnoreCase)))
            return "EU";

        if (regionCodes.Any(r => r.Contains("west", StringComparison.OrdinalIgnoreCase)))
            return "West";

        return regionCodes.FirstOrDefault() ?? "Unknown Region";
    }

    private class ServerCandidate
    {
        public required MatchLabel Match { get; set; }
        public required string ServerIp { get; set; }
        public double AverageLatency { get; set; }
        public double MaxLatency { get; set; }
        public int PlayersWithData { get; set; }
    }
}
