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
    public async Task<TokenResponse?> GetNakamaTokenAsync()
    {
        using var client = factory.CreateClient("Nakama");

        var tokenRequest = new TokenRequest
        {
            Username = nakamaOptions.Value.Username,
            Password = nakamaOptions.Value.Password
        };

        var url = $"{nakamaOptions.Value.BaseUrl}{nakamaOptions.Value.AuthEndpoint}&http_key={nakamaOptions.Value.HttpKey}";

        try
        {
            var response = await client.PostAsJsonAsync(url, tokenRequest);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var tokenResponse = JsonSerializer.Deserialize<TokenResponse>(responseContent);
                return tokenResponse;
            }

            logger.LogError(
                "Nakama token request failed. Status: {StatusCode}, Response: {Response}",
                response.StatusCode, responseContent);
            return null;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "HttpRequestException while getting Nakama token");
            return null;
        }
        catch (TaskCanceledException ex)
        {
            logger.LogError(ex, "Request timed out while getting Nakama token");
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error while getting Nakama token");
            return null;
        }
    }

    public async Task<string?> GetNakamaIdAsync(string discordId, TokenResponse token)
    {
        using var client = factory.CreateClient("Nakama");

        var url = $"{nakamaOptions.Value.BaseUrl}{nakamaOptions.Value.LookupEndpoint}";
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        var content = new StringContent(
            $"{{\"discord_id\": \"{discordId}\"}}",
            null,
            "application/json");

        try
        {
            var response = await client.PostAsync(url, content);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                return responseContent.Split('"')[3];
            }

            logger.LogError(
                "Nakama ID lookup failed for Discord ID {DiscordId}. Status: {StatusCode}, Response: {Response}",
                discordId, response.StatusCode, responseContent);
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
        using var client = factory.CreateClient("Nakama");

        var url = $"{nakamaOptions.Value.BaseUrl}{nakamaOptions.Value.MatchEndpoint}";
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

        try
        {
            var response = await client.GetAsync(url);

            if (response.IsSuccessStatusCode)
            {
                var responseContent = await response.Content.ReadAsStringAsync();
                var serverList = JsonSerializer.Deserialize<NakamaMatches>(responseContent);

                var matchCount = serverList?.Labels.Count ?? 0;
                logger.LogInformation("Nakama matches retrieved successfully. Count: {MatchCount}", matchCount);

                return serverList;
            }

            var errorContent = await response.Content.ReadAsStringAsync();
            logger.LogError(
                "Failed to retrieve Nakama matches. Status: {StatusCode}, Response: {Response}",
                response.StatusCode, errorContent);
            return null;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "HttpRequestException during Nakama matches retrieval");
            return null;
        }
        catch (TaskCanceledException ex)
        {
            logger.LogError(ex, "Request timed out during Nakama matches retrieval");
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error during Nakama matches retrieval");
            return null;
        }
    }

    public async Task<GameServerLatencyModel?> GetUserLatencyDataAsync(string nakamaId, TokenResponse token)
    {
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

        try
        {
            var response = await client.PostAsJsonAsync(url, storageRequest);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError(
                    "Failed to get latency data for Nakama user {NakamaId}. Status: {StatusCode}, Response: {Response}",
                    nakamaId, response.StatusCode, responseContent);
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
            return latencyData;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception while fetching latency data for Nakama user {NakamaId}", nakamaId);
            return null;
        }
    }

    public MatchLabel? GetEmptyEchoMatchAsync(NakamaMatches matches, bool containEu = false)
    {
        // Remove unwanted matches from labels
        matches.Labels = matches.Labels
            .Where(x => !x.Id.Contains(nakamaOptions.Value.ExcludedBroadcasterId))
            .ToList();

        // Define prioritized search conditions
        var searchConditions = new List<Func<MatchLabel, bool>>
        {
            x => x.Broadcaster.RegionCodes.Contains("redacted-region") && x.LobbyType.Contains("unassigned"),
            x => containEu && x.Broadcaster.Tags.Contains("180hz") && x.Broadcaster.RegionCodes.Contains("redacted-region") && x.LobbyType.Contains("unassigned"),
            x => x.Broadcaster.RegionCodes.Contains("redacted-region") && x.LobbyType.Contains("unassigned"),
            x => x.Broadcaster.RegionCodes.Contains("redacted-region") && x.LobbyType.Contains("unassigned"),
            x => x.Broadcaster.RegionCodes.Contains("redacted-region") && x.LobbyType.Contains("unassigned"),
            x => x.Broadcaster.RegionCodes.Contains("redacted-region") && x.LobbyType.Contains("unassigned"),
            x => x.Broadcaster.RegionCodes.Contains("180hz") && x.Broadcaster.Tags.Contains("ranked-central") && x.LobbyType.Contains("unassigned"),
            x => x.Broadcaster.RegionCodes.Contains("comp") && x.LobbyType.Contains("unassigned"),
            x => x.Broadcaster.Endpoint.Contains("0.0.0.0") && x.LobbyType.Contains("unassigned"),
            x => x.Broadcaster.Endpoint.Contains("0.0.0.0") && x.LobbyType.Contains("unassigned"),
            x => x.Broadcaster.Endpoint.Contains("0.0.0.0") && x.LobbyType.Contains("unassigned"),
            x => x.Broadcaster.Endpoint.Contains("0.0.0.0") && x.LobbyType.Contains("unassigned"),
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
                logger.LogInformation(
                    "Echo match prepared successfully. Queue: {QueueName}, Response: {Response}",
                    queueName, responseContent);
                return ExtractMatchId(responseContent);
            }

            logger.LogError(
                "Failed to prepare Echo match. Status: {StatusCode}, Response: {Response}",
                response.StatusCode, responseContent);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception during PrepareEchoMatchAsync");
            return null;
        }
    }

    public async Task<string?> BackupPrepareEchoMatchAsync(
        MatchLabel match, TokenResponse token, List<TeamOrientation>? players, string queueName)
    {
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
                logger.LogInformation(
                    "Backup Echo match prepared successfully. Queue: {QueueName}, Response: {Response}",
                    queueName, responseContent);
                return ExtractMatchId(responseContent);
            }

            logger.LogError(
                "Failed to prepare backup Echo match. Status: {StatusCode}, Response: {Response}",
                response.StatusCode, responseContent);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception during BackupPrepareEchoMatchAsync");
            return null;
        }
    }

    public async Task<(bool Result, string ResponseContent)> AssignPlayersToEchoMatchAsync(
        string userId, string sessionId, TokenResponse token, string playerColor)
    {
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
                logger.LogInformation("Player assigned successfully. Response: {Response}", responseContent);
                return (true, responseContent);
            }

            logger.LogError(
                "Failed to assign player to Echo match. Status: {StatusCode}, Response: {Response}",
                response.StatusCode, responseContent);
            return (false, responseContent);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception during AssignPlayersToEchoMatchAsync");
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
