using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace EchoRankedServerBot.Services;

public class NeatQueueService(
    IHttpClientFactory factory,
    ILogger<NeatQueueService> logger)
{
    private const int MaxRetries = 3;
    private static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(2);

    /// <summary>
    /// Rewards the MVP player with +7 MMR via the NeatQueue API.
    /// Retries up to 3 times with a 2-second delay between attempts.
    /// </summary>
    public async Task<bool> RewardMvpAsync(ulong memberId, ulong channelId)
    {
        var payload = new
        {
            channel_id = channelId,
            mmr = 7,
            user_id = memberId.ToString()
        };

        const string endpoint = "https://api.neatqueue.com/api/v2/add/mmr";
        var jsonString = JsonSerializer.Serialize(payload);

        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            logger.LogDebug(
                "Posting MVP reward attempt {Attempt}/{MaxRetries} for member {MemberId} in channel {ChannelId}",
                attempt, MaxRetries, memberId, channelId);

            try
            {
                using var client = factory.CreateClient("NeatQueue");

                var content = new StringContent(jsonString, Encoding.UTF8, "application/json");
                var response = await client.PostAsync(endpoint, content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    logger.LogWarning(
                        "NeatQueue returned status {StatusCode} on attempt {Attempt}/{MaxRetries} for member {MemberId}. Response: {Response}",
                        response.StatusCode, attempt, MaxRetries, memberId, Truncate(responseContent));
                }
                else if (responseContent.Contains("MMR"))
                {
                    logger.LogInformation(
                        "MVP reward posted successfully for member {MemberId} in channel {ChannelId}. Response: {Response}",
                        memberId, channelId, responseContent);
                    return true;
                }
                else
                {
                    logger.LogWarning(
                        "MVP reward attempt {Attempt}/{MaxRetries} failed for member {MemberId}. Response: {Response}",
                        attempt, MaxRetries, memberId, Truncate(responseContent));
                }
            }
            catch (HttpRequestException ex)
            {
                logger.LogError(ex,
                    "HttpRequestException on MVP reward attempt {Attempt}/{MaxRetries} for member {MemberId} at endpoint {Endpoint}",
                    attempt, MaxRetries, memberId, endpoint);
            }
            catch (TaskCanceledException ex)
            {
                logger.LogError(ex,
                    "MVP reward attempt {Attempt}/{MaxRetries} timed out for member {MemberId} at endpoint {Endpoint}",
                    attempt, MaxRetries, memberId, endpoint);
            }
            catch (JsonException ex)
            {
                logger.LogError(ex,
                    "Failed to serialize or parse data on MVP reward attempt {Attempt}/{MaxRetries} for member {MemberId}",
                    attempt, MaxRetries, memberId);
            }
            catch (Exception ex)
            {
                logger.LogError(ex,
                    "MVP reward attempt {Attempt}/{MaxRetries} threw an exception for member {MemberId}",
                    attempt, MaxRetries, memberId);
            }

            if (attempt < MaxRetries)
            {
                await Task.Delay(RetryDelay);
            }
        }

        logger.LogError(
            "All {MaxRetries} MVP reward attempts exhausted for member {MemberId} in channel {ChannelId}",
            MaxRetries, memberId, channelId);
        return false;
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
}
