using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EchoRankedServerBot.Configuration;
using EchoRankedServerBot.Models.EchoApi;
using EchoRankedServerBot.Models.Nakama;
using EchoRankedServerBot.Models.Streaming;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EchoRankedServerBot.Services;

public class StreamingApiService(
    IHttpClientFactory factory,
    IOptions<NakamaOptions> nakamaOptions,
    ILogger<StreamingApiService> logger)
{
    /// <summary>
    /// Converts streaming API SessionData to the existing EchoVrApiSession model.
    /// This allows reuse of existing code that depends on EchoVrApiSession.
    /// </summary>
    private static EchoVrApiSession? ConvertToEchoVrApiSession(SessionData? sessionData)
    {
        if (sessionData is null)
            return null;

        return new EchoVrApiSession
        {
            SessionId = sessionData.SessionId,
            GameClockDisplay = sessionData.GameClockDisplay,
            GameStatus = sessionData.GameStatus,
            MatchType = sessionData.MatchType,
            MapName = sessionData.MapName,
            Disc = sessionData.Disc,
            OrangePoints = sessionData.OrangePoints,
            BluePoints = sessionData.BluePoints,
            OrangeRoundScore = sessionData.OrangeRoundScore,
            BlueRoundScore = sessionData.BlueRoundScore,
            TotalRoundCount = sessionData.TotalRoundCount,
            Teams = sessionData.Teams,
            Possession = sessionData.Possession,
            GameClock = sessionData.GameClock,
            LastScore = sessionData.LastScore,
            LastThrow = sessionData.LastThrow,
            Pause = sessionData.Pause,
            PrivateMatch = sessionData.PrivateMatch,
            TournamentMatch = sessionData.TournamentMatch
        };
    }

    /// <summary>
    /// Fetches the latest frame from the streaming API and returns its session data.
    /// </summary>
    private async Task<SessionData?> GetLatestSessionDataAsync(string matchId, TokenResponse token)
    {
        if (string.IsNullOrWhiteSpace(nakamaOptions.Value.BaseUrl))
        {
            logger.LogError("Required configuration value {SettingName} is missing or empty.", "Nakama:BaseUrl");
            return null;
        }

        if (string.IsNullOrWhiteSpace(nakamaOptions.Value.StreamingEndpoint))
        {
            logger.LogError("Required configuration value {SettingName} is missing or empty.", "Nakama:StreamingEndpoint");
            return null;
        }

        var streamingUrl = $"{nakamaOptions.Value.BaseUrl}{nakamaOptions.Value.StreamingEndpoint}";

        logger.LogDebug("Requesting streaming API session data for match {MatchId}", matchId);

        try
        {
            using var client = factory.CreateClient("Nakama");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

            var requestBody = JsonSerializer.Serialize(new { match_id = matchId });
            var content = new StringContent(requestBody, Encoding.UTF8, "application/json");

            var response = await client.PostAsync(streamingUrl, content);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var streamingResponse = JsonSerializer.Deserialize<LobbySessionEventsResponse>(responseContent);

                if (streamingResponse?.Events is { Count: > 0 })
                {
                    var latestFrame = streamingResponse.Events.LastOrDefault()?.Frame;

                    if (latestFrame?.Session is not null)
                        return latestFrame.Session;

                    logger.LogWarning(
                        "Streaming API response for match {MatchId} did not contain session data in the latest frame", matchId);
                    return null;
                }

                logger.LogWarning("Streaming API response for match {MatchId} contained no events", matchId);
                return null;
            }

            var truncatedResponse = Truncate(responseContent);

            logger.LogError(
                "Streaming API returned status {StatusCode} for match {MatchId}, so no session data could be retrieved. Response: {Response}",
                response.StatusCode, matchId, truncatedResponse);

            return null;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "HttpRequestException fetching streaming API session for match {MatchId}", matchId);
            return null;
        }
        catch (TaskCanceledException ex)
        {
            logger.LogWarning(ex, "Streaming API request timed out for match {MatchId}", matchId);
            return null;
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to parse streaming API session response for match {MatchId}", matchId);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching streaming API session for match {MatchId}", matchId);
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

    /// <summary>
    /// Checks if the match has ended by looking for a matchEnded event in the frames.
    /// </summary>
    public async Task<bool> HasMatchEndedAsync(string matchId, TokenResponse token)
    {
        if (string.IsNullOrWhiteSpace(nakamaOptions.Value.BaseUrl))
        {
            logger.LogError("Required configuration value {SettingName} is missing or empty.", "Nakama:BaseUrl");
            return false;
        }

        if (string.IsNullOrWhiteSpace(nakamaOptions.Value.StreamingEndpoint))
        {
            logger.LogError("Required configuration value {SettingName} is missing or empty.", "Nakama:StreamingEndpoint");
            return false;
        }

        var streamingUrl = $"{nakamaOptions.Value.BaseUrl}{nakamaOptions.Value.StreamingEndpoint}";

        logger.LogDebug("Checking whether match {MatchId} has ended", matchId);

        try
        {
            using var client = factory.CreateClient("Nakama");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

            var requestBody = JsonSerializer.Serialize(new { match_id = matchId });
            var content = new StringContent(requestBody, Encoding.UTF8, "application/json");

            var response = await client.PostAsync(streamingUrl, content);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError(
                    "Streaming API returned status {StatusCode} while checking match end for {MatchId}, so match end status is assumed false. Response: {Response}",
                    response.StatusCode, matchId, Truncate(responseContent));
                return false;
            }

            var streamingResponse = JsonSerializer.Deserialize<LobbySessionEventsResponse>(responseContent);

            if (streamingResponse?.Events is not null)
            {
                return streamingResponse.Events.Any(e =>
                    e.Frame?.Events is not null &&
                    e.Frame.Events.Any(evt => evt.IsMatchEnded));
            }

            logger.LogWarning("Streaming API response for match {MatchId} contained no events when checking match end status", matchId);
            return false;
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "HttpRequestException while checking if match ended for {MatchId}", matchId);
            return false;
        }
        catch (TaskCanceledException ex)
        {
            logger.LogWarning(ex, "Request timed out while checking if match ended for {MatchId}", matchId);
            return false;
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to parse streaming API response while checking if match ended for {MatchId}", matchId);
            return false;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error checking if match ended for {MatchId}", matchId);
            return false;
        }
    }

    /// <summary>
    /// Gets the latest session data and converts it to EchoVrApiSession format.
    /// </summary>
    public async Task<EchoVrApiSession?> GetEchoApiFromStreamingAsync(string matchId, TokenResponse token)
    {
        var sessionData = await GetLatestSessionDataAsync(matchId, token);
        var session = ConvertToEchoVrApiSession(sessionData);

        if (session is null)
            logger.LogWarning("No Echo VR session could be built from streaming data for match {MatchId}", matchId);

        return session;
    }
}
