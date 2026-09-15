using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using EchoRankedServerBot.Configuration;
using EchoRankedServerBot.Models.Latency;
using EchoRankedServerBot.Models.Match;
using EchoRankedServerBot.Models.Nakama;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EchoRankedServerBot.Services;

public class NakamaApiService(
    IHttpClientFactory factory,
    IOptions<NakamaOptions> nakamaOptions,
    IOptions<BotOptions> botOptions,
    ILogger<NakamaApiService> logger)
{
    /// <summary>
    /// Verifies that a required configuration value is present. Logs an error naming the setting
    /// when it is missing so callers can return a failure value instead of throwing later.
    /// </summary>
    private bool RequireConfig(string? value, string settingName)
    {
        if (!string.IsNullOrWhiteSpace(value))
            return true;

        logger.LogError("Required configuration value {SettingName} is missing or empty.", settingName);
        return false;
    }

    public async Task<TokenResponse?> GetNakamaTokenAsync()
    {
        if (!RequireConfig(nakamaOptions.Value.BaseUrl, "Nakama:BaseUrl") ||
            !RequireConfig(nakamaOptions.Value.AuthEndpoint, "Nakama:AuthEndpoint") ||
            !RequireConfig(nakamaOptions.Value.HttpKey, "Nakama:HttpKey") ||
            !RequireConfig(nakamaOptions.Value.Username, "Nakama:Username") ||
            !RequireConfig(nakamaOptions.Value.Password, "Nakama:Password"))
        {
            return null;
        }

        using var client = factory.CreateClient("Nakama");

        var tokenRequest = new TokenRequest
        {
            Username = nakamaOptions.Value.Username,
            Password = nakamaOptions.Value.Password
        };

        var url = $"{nakamaOptions.Value.BaseUrl}{nakamaOptions.Value.AuthEndpoint}&http_key={nakamaOptions.Value.HttpKey}";

        logger.LogDebug("Requesting Nakama authentication token from {Endpoint}", nakamaOptions.Value.AuthEndpoint);

        try
        {
            var response = await client.PostAsJsonAsync(url, tokenRequest);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(responseContent);
                if (tokenResponse is null)
                {
                    logger.LogWarning("Nakama token response deserialized to null for endpoint {Endpoint}", nakamaOptions.Value.AuthEndpoint);
                    return null;
                }

                logger.LogInformation("Nakama authentication token acquired successfully.");
                return tokenResponse;
            }

            var truncated = Truncate(responseContent);
            logger.LogError(
                "Nakama token request failed. Status: {StatusCode}, Endpoint: {Endpoint}, Response: {Response}",
                response.StatusCode, nakamaOptions.Value.AuthEndpoint, truncated);
            return null;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "HttpRequestException while getting Nakama token from {Endpoint}", nakamaOptions.Value.AuthEndpoint);
            return null;
        }
        catch (TaskCanceledException ex)
        {
            logger.LogError(ex, "Request timed out while getting Nakama token from {Endpoint}", nakamaOptions.Value.AuthEndpoint);
            return null;
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to parse Nakama token response from {Endpoint}", nakamaOptions.Value.AuthEndpoint);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error while getting Nakama token from {Endpoint}", nakamaOptions.Value.AuthEndpoint);
            return null;
        }
    }

    /// <summary>
    /// Truncates a response body to the first 500 characters for safe logging.
    /// </summary>
    private static string Truncate(string? content)
    {
        if (string.IsNullOrEmpty(content))
            return string.Empty;

        var trimmed = content.Trim();
        return trimmed.Length > 500 ? trimmed[..500] : trimmed;
    }

    public async Task<string?> GetNakamaIdAsync(string discordId, TokenResponse token)
    {
        if (!RequireConfig(nakamaOptions.Value.BaseUrl, "Nakama:BaseUrl") ||
            !RequireConfig(nakamaOptions.Value.LookupEndpoint, "Nakama:LookupEndpoint"))
        {
            return null;
        }

        using var client = factory.CreateClient("Nakama");

        var url = $"{nakamaOptions.Value.BaseUrl}{nakamaOptions.Value.LookupEndpoint}";
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        var content = new StringContent(
            $"{{\"discord_id\": \"{discordId}\"}}",
            null,
            "application/json");

        logger.LogDebug("Looking up Nakama ID for Discord ID {DiscordId}", discordId);

        try
        {
            var response = await client.PostAsync(url, content);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var parts = responseContent.Split('"');
                if (parts.Length < 4)
                {
                    logger.LogWarning(
                        "Nakama ID lookup response for Discord ID {DiscordId} did not contain the expected id field. Response: {Response}",
                        discordId, Truncate(responseContent));
                    return null;
                }

                logger.LogInformation("Resolved Nakama ID for Discord ID {DiscordId}", discordId);
                return parts[3];
            }

            logger.LogError(
                "Nakama ID lookup failed for Discord ID {DiscordId}. Status: {StatusCode}, Response: {Response}",
                discordId, response.StatusCode, Truncate(responseContent));
            return null;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "HttpRequestException during Nakama ID lookup for Discord ID {DiscordId}", discordId);
            return null;
        }
        catch (TaskCanceledException ex)
        {
            logger.LogError(ex, "Request timed out during Nakama ID lookup for Discord ID {DiscordId}", discordId);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error during Nakama ID lookup for Discord ID {DiscordId}", discordId);
            return null;
        }
    }

    public async Task<NakamaMatches?> GetNakamaMatchesAsync(TokenResponse token)
    {
        if (!RequireConfig(nakamaOptions.Value.BaseUrl, "Nakama:BaseUrl") ||
            !RequireConfig(nakamaOptions.Value.MatchEndpoint, "Nakama:MatchEndpoint"))
        {
            return null;
        }

        using var client = factory.CreateClient("Nakama");

        var url = $"{nakamaOptions.Value.BaseUrl}{nakamaOptions.Value.MatchEndpoint}";
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        logger.LogDebug("Requesting Nakama match list from {Endpoint}", nakamaOptions.Value.MatchEndpoint);

        try
        {
            var response = await client.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                var serverList = JsonSerializer.Deserialize<NakamaMatches>(responseContent);

                if (serverList is null)
                {
                    logger.LogWarning(
                        "Nakama match list deserialized to null for endpoint {Endpoint}", nakamaOptions.Value.MatchEndpoint);
                    return null;
                }

                logger.LogInformation("Nakama matches retrieved successfully. Count: {MatchCount}", serverList.Labels.Count);

                return serverList;
            }

            var errorContent = await response.Content.ReadAsStringAsync();
            logger.LogError(
                "Failed to retrieve Nakama matches. Status: {StatusCode}, Endpoint: {Endpoint}, Response: {Response}",
                response.StatusCode, nakamaOptions.Value.MatchEndpoint, Truncate(errorContent));
            return null;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "HttpRequestException during Nakama matches retrieval from {Endpoint}", nakamaOptions.Value.MatchEndpoint);
            return null;
        }
        catch (TaskCanceledException ex)
        {
            logger.LogError(ex, "Request timed out during Nakama matches retrieval from {Endpoint}", nakamaOptions.Value.MatchEndpoint);
            return null;
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to parse Nakama match list from {Endpoint}", nakamaOptions.Value.MatchEndpoint);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error during Nakama matches retrieval from {Endpoint}", nakamaOptions.Value.MatchEndpoint);
            return null;
        }
    }

    public async Task<GameServerLatencyModel?> GetUserLatencyDataAsync(string nakamaId, TokenResponse token)
    {
        if (!RequireConfig(nakamaOptions.Value.BaseUrl, "Nakama:BaseUrl") ||
            !RequireConfig(nakamaOptions.Value.StorageEndpoint, "Nakama:StorageEndpoint"))
        {
            return null;
        }

        using var client = factory.CreateClient("Nakama");

        var url = $"{nakamaOptions.Value.BaseUrl}{nakamaOptions.Value.StorageEndpoint}";
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        var storageRequest = new NakamaStorageRequest
        {
            CollectionData =
            [
                new NakamaCollectionRequestData
                {
                    Collection = "LatencyHistory",
                    Key = "store",
                    UserId = nakamaId
                }
            ]
        };

        logger.LogDebug("Requesting latency history for Nakama user {NakamaId}", nakamaId);

        try
        {
            var response = await client.PostAsJsonAsync(url, storageRequest);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError(
                    "Failed to get latency data for Nakama user {NakamaId}. Status: {StatusCode}, Response: {Response}",
                    nakamaId, response.StatusCode, Truncate(responseContent));
                return null;
            }

            var storageResponse = JsonSerializer.Deserialize<NakamaCollectionResponse>(
                responseContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var latencyObject = storageResponse?.Objects.FirstOrDefault();
            if (latencyObject is null || string.IsNullOrWhiteSpace(latencyObject.Value))
            {
                logger.LogWarning("No latency history found for Nakama user {NakamaId}", nakamaId);
                return null;
            }

            var latencyData = JsonSerializer.Deserialize<GameServerLatencyModel>(
                latencyObject.Value, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (latencyData is null)
            {
                logger.LogWarning("Latency data deserialized to null for Nakama user {NakamaId}", nakamaId);
                return null;
            }

            logger.LogDebug("Latency history retrieved for Nakama user {NakamaId}", nakamaId);
            return latencyData;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "HttpRequestException while fetching latency data for Nakama user {NakamaId}", nakamaId);
            return null;
        }
        catch (TaskCanceledException ex)
        {
            logger.LogError(ex, "Request timed out while fetching latency data for Nakama user {NakamaId}", nakamaId);
            return null;
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to parse latency data for Nakama user {NakamaId}", nakamaId);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception while fetching latency data for Nakama user {NakamaId}", nakamaId);
            return null;
        }
    }

    public MatchLabel? GetEmptyEchoMatchAsync(NakamaMatches matches, bool containEu = false)
    {
        logger.LogDebug("Searching for an empty Echo match among {Count} candidates. ContainEu: {ContainEu}", matches.Labels.Count, containEu);

        // Remove unwanted matches from labels
        matches.Labels = matches.Labels
            .Where(x => !x.Id.Contains(nakamaOptions.Value.ExcludedBroadcasterId))
            .ToList();

        // Define prioritized search conditions
        var searchConditions = new List<Func<MatchLabel, bool>>
        {
            x => x.Broadcaster.RegionCodes.Contains(botOptions.Value.ChicagoRegionCode) && x.LobbyType.Contains("unassigned"),
            x => containEu && x.Broadcaster.Tags.Contains("180hz") && x.Broadcaster.RegionCodes.Contains(botOptions.Value.ChicagoRegionCode) && x.LobbyType.Contains("unassigned"),
            x => x.Broadcaster.RegionCodes.Contains(botOptions.Value.RankedCompRegionCode) && x.LobbyType.Contains("unassigned"),
            x => x.Broadcaster.RegionCodes.Contains(botOptions.Value.DallasRegionCode) && x.LobbyType.Contains("unassigned"),
            x => x.Broadcaster.RegionCodes.Contains(botOptions.Value.OmahaRegionCode) && x.LobbyType.Contains("unassigned"),
            x => x.Broadcaster.RegionCodes.Contains(botOptions.Value.PgvDcCevrRegionCode) && x.LobbyType.Contains("unassigned"),
            x => x.Broadcaster.RegionCodes.Contains("180hz") && x.Broadcaster.Tags.Contains("ranked-central") && x.LobbyType.Contains("unassigned"),
            x => x.Broadcaster.RegionCodes.Contains("comp") && x.LobbyType.Contains("unassigned"),
            x => x.Broadcaster.Endpoint.Contains(botOptions.Value.NebraskaServerIp) && x.LobbyType.Contains("unassigned"),
            x => x.Broadcaster.Endpoint.Contains(botOptions.Value.FallbackServerIp) && x.LobbyType.Contains("unassigned"),
            x => x.Broadcaster.Endpoint.Contains(botOptions.Value.PennsylvaniaServerIp) && x.LobbyType.Contains("unassigned"),
            x => x.Broadcaster.Endpoint.Contains(botOptions.Value.KansasServerIp) && x.LobbyType.Contains("unassigned"),
            x => x.LobbyType.Contains("unassigned")
        };

        MatchLabel? empty = null;

        foreach (var condition in searchConditions)
        {
            empty = matches.Labels.FirstOrDefault(condition);
            if (empty is not null)
                break;
        }

        if (empty is not null)
        {
            var json = JsonSerializer.Serialize(empty, new JsonSerializerOptions { WriteIndented = true });
            logger.LogInformation("Empty Echo match found: {MatchJson}", json);
        }
        else
        {
            logger.LogWarning("No empty Echo match found");
        }

        return empty;
    }

    public async Task<string?> PrepareEchoMatchAsync(
        MatchLabel match, TokenResponse token, List<TeamOrientation>? players, string queueName)
    {
        if (!RequireConfig(nakamaOptions.Value.BaseUrl, "Nakama:BaseUrl") ||
            !RequireConfig(nakamaOptions.Value.PrepareEndpoint, "Nakama:PrepareEndpoint"))
        {
            return null;
        }

        using var client = factory.CreateClient("Nakama");

        var url = $"{nakamaOptions.Value.BaseUrl}{nakamaOptions.Value.PrepareEndpoint}";
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        var teamAlignments = BuildTeamAlignments(players);

        var prepareRequest = new PrepareEchoMatchRequest
        {
            Id = match.Id,
            GuildId = botOptions.Value.PrimaryGuildId,
            OwnerId = botOptions.Value.SpawnedBy,
            TeamAlignments = teamAlignments,
            Mode = "echo_arena_private"
        };

        var requestData = JsonSerializer.Serialize(prepareRequest, new JsonSerializerOptions { WriteIndented = true });
        logger.LogInformation("Preparing Echo match. Endpoint: {Url}, Request: {RequestData}", url, requestData);

        try
        {
            var response = await client.PostAsJsonAsync(url, prepareRequest);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var matchId = ExtractMatchId(responseContent);
                if (string.IsNullOrEmpty(matchId))
                {
                    logger.LogWarning(
                        "Echo match prepare response for queue {QueueName} did not contain a match id. Response: {Response}",
                        queueName, Truncate(responseContent));
                    return null;
                }

                logger.LogInformation(
                    "Echo match {MatchId} prepared successfully on server {Endpoint} for queue {QueueName}",
                    matchId, url, queueName);
                return matchId;
            }

            logger.LogError(
                "Failed to prepare Echo match. Status: {StatusCode}, Endpoint: {Endpoint}, Response: {Response}",
                response.StatusCode, url, Truncate(responseContent));
            return null;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "HttpRequestException during PrepareEchoMatchAsync for queue {QueueName}", queueName);
            return null;
        }
        catch (TaskCanceledException ex)
        {
            logger.LogError(ex, "Request timed out during PrepareEchoMatchAsync for queue {QueueName}", queueName);
            return null;
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to serialize or parse data during PrepareEchoMatchAsync for queue {QueueName}", queueName);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception during PrepareEchoMatchAsync for queue {QueueName}", queueName);
            return null;
        }
    }

    public async Task<string?> BackupPrepareEchoMatchAsync(
        MatchLabel match, TokenResponse token, List<TeamOrientation>? players, string queueName)
    {
        if (!RequireConfig(nakamaOptions.Value.BaseUrl, "Nakama:BaseUrl") ||
            !RequireConfig(nakamaOptions.Value.PrepareEndpoint, "Nakama:PrepareEndpoint"))
        {
            return null;
        }

        using var client = factory.CreateClient("Nakama");

        var url = $"{nakamaOptions.Value.BaseUrl}{nakamaOptions.Value.PrepareEndpoint}";
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        var teamAlignments = BuildTeamAlignments(players);

        var prepareRequest = new PrepareEchoMatchRequest
        {
            Id = match.Id,
            GuildId = botOptions.Value.BackupGuildId,
            OwnerId = botOptions.Value.SpawnedBy,
            TeamAlignments = teamAlignments,
            Mode = "echo_arena_private"
        };

        var requestData = JsonSerializer.Serialize(prepareRequest, new JsonSerializerOptions { WriteIndented = true });
        logger.LogInformation("Preparing backup Echo match. Endpoint: {Url}, Request: {RequestData}", url, requestData);

        try
        {
            var response = await client.PostAsJsonAsync(url, prepareRequest);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var matchId = ExtractMatchId(responseContent);
                if (string.IsNullOrEmpty(matchId))
                {
                    logger.LogWarning(
                        "Backup Echo match prepare response for queue {QueueName} did not contain a match id. Response: {Response}",
                        queueName, Truncate(responseContent));
                    return null;
                }

                logger.LogInformation(
                    "Backup Echo match {MatchId} prepared successfully on server {Endpoint} for queue {QueueName}",
                    matchId, url, queueName);
                return matchId;
            }

            logger.LogError(
                "Failed to prepare backup Echo match. Status: {StatusCode}, Endpoint: {Endpoint}, Response: {Response}",
                response.StatusCode, url, Truncate(responseContent));
            return null;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "HttpRequestException during BackupPrepareEchoMatchAsync for queue {QueueName}", queueName);
            return null;
        }
        catch (TaskCanceledException ex)
        {
            logger.LogError(ex, "Request timed out during BackupPrepareEchoMatchAsync for queue {QueueName}", queueName);
            return null;
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to serialize or parse data during BackupPrepareEchoMatchAsync for queue {QueueName}", queueName);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception during BackupPrepareEchoMatchAsync for queue {QueueName}", queueName);
            return null;
        }
    }

    public async Task<(bool Result, string ResponseContent)> AssignPlayersToEchoMatchAsync(
        string userId, string sessionId, TokenResponse token, string playerColor)
    {
        if (!RequireConfig(nakamaOptions.Value.BaseUrl, "Nakama:BaseUrl") ||
            !RequireConfig(nakamaOptions.Value.AssignEndpoint, "Nakama:AssignEndpoint"))
        {
            return (false, string.Empty);
        }

        using var client = factory.CreateClient("Nakama");

        var url = $"{nakamaOptions.Value.BaseUrl}{nakamaOptions.Value.AssignEndpoint}";
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        var assignRequest = new AssignPlayerRequest
        {
            UserId = userId,
            MatchId = sessionId.ToLower(),
            Role = playerColor
        };

        var requestData = JsonSerializer.Serialize(assignRequest, new JsonSerializerOptions { WriteIndented = true });
        logger.LogInformation("Assigning player to Echo match. Endpoint: {Url}, Request: {RequestData}", url, requestData);

        try
        {
            var response = await client.PostAsJsonAsync(url, assignRequest);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                logger.LogInformation(
                    "Player {UserId} assigned to Echo match {MatchId} as {PlayerColor}", userId, sessionId, playerColor);
                return (true, responseContent);
            }

            logger.LogError(
                "Failed to assign player {UserId} to Echo match {MatchId}. Status: {StatusCode}, Response: {Response}",
                userId, sessionId, response.StatusCode, Truncate(responseContent));
            return (false, responseContent);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "HttpRequestException during AssignPlayersToEchoMatchAsync for player {UserId} in match {MatchId}", userId, sessionId);
            return (false, ex.Message);
        }
        catch (TaskCanceledException ex)
        {
            logger.LogError(ex, "Request timed out during AssignPlayersToEchoMatchAsync for player {UserId} in match {MatchId}", userId, sessionId);
            return (false, ex.Message);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to serialize or parse data during AssignPlayersToEchoMatchAsync for player {UserId} in match {MatchId}", userId, sessionId);
            return (false, ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception during AssignPlayersToEchoMatchAsync for player {UserId} in match {MatchId}", userId, sessionId);
            return (false, ex.Message);
        }
    }

    private static Dictionary<string, string> BuildTeamAlignments(List<TeamOrientation>? players)
    {
        var teamAlignments = new Dictionary<string, string>();

        if (players is null)
            return teamAlignments;

        foreach (var player in players)
        {
            if (player.DiscordId is null)
                continue;

            if (player.TeamName == "orange")
                teamAlignments.Add(player.DiscordId, "0");
            if (player.TeamName == "blue")
                teamAlignments.Add(player.DiscordId, "1");
        }

        return teamAlignments;
    }

    private static string ExtractMatchId(string responseContent)
    {
        var parts = responseContent.Split('"');
        return parts.Length >= 4 ? parts[3].Split('.')[0].ToUpper() : string.Empty;
    }
}
