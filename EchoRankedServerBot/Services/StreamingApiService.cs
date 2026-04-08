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
        var streamingUrl = $"{nakamaOptions.Value.BaseUrl}{nakamaOptions.Value.StreamingEndpoint}";

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
                }

                return null;
            }

            var truncatedResponse = responseContent.Length > 500
                ? responseContent[..500] + "..."
                : responseContent;

            logger.LogError(
                "Failed to fetch streaming API session. MatchId: {MatchId}, StatusCode: {StatusCode}, Response: {Response}",
                matchId, response.StatusCode, truncatedResponse);

            return null;
        }
        catch (TaskCanceledException)
        {
            logger.LogWarning("Streaming API request timed out for match {MatchId}", matchId);
            return null;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching streaming API session for match {MatchId}", matchId);
            return null;
        }
    }

    /// <summary>
    /// Checks if the match has ended by looking for a matchEnded event in the frames.
    /// </summary>
    public async Task<bool> HasMatchEndedAsync(string matchId, TokenResponse token)
    {
        var streamingUrl = $"{nakamaOptions.Value.BaseUrl}{nakamaOptions.Value.StreamingEndpoint}";

        try
        {
            using var client = factory.CreateClient("Nakama");
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token.Token);

            var requestBody = JsonSerializer.Serialize(new { match_id = matchId });
            var content = new StringContent(requestBody, Encoding.UTF8, "application/json");

            var response = await client.PostAsync(streamingUrl, content);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode) return false;
            var streamingResponse = JsonSerializer.Deserialize<LobbySessionEventsResponse>(responseContent);

            if (streamingResponse?.Events is not null)
            {
                return streamingResponse.Events.Any(e =>
                    e.Frame?.Events is not null &&
                    e.Frame.Events.Any(evt => evt.IsMatchEnded));
            }

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
        return ConvertToEchoVrApiSession(sessionData);
    }
}
