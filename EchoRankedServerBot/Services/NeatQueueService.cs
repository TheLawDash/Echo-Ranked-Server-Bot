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

        var jsonString = JsonSerializer.Serialize(payload);

        for (var attempt = 1; attempt <= MaxRetries; attempt++)
        {
            try
            {
                using var client = factory.CreateClient("NeatQueue");

                var content = new StringContent(jsonString, Encoding.UTF8, "application/json");
                var response = await client.PostAsync("https://api.neatqueue.com/api/v2/add/mmr", content);
                var responseContent = await response.Content.ReadAsStringAsync();

                if (responseContent.Contains("MMR"))
                {
                    logger.LogInformation(
                        "MVP reward posted successfully for member {MemberId} in channel {ChannelId}. Response: {Response}",
                        memberId, channelId, responseContent);
                    return true;
                }

                logger.LogWarning(
                    "MVP reward attempt {Attempt}/{MaxRetries} failed for member {MemberId}. Response: {Response}",
                    attempt, MaxRetries, memberId, responseContent);
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
}
