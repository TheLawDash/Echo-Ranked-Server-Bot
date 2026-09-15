using System.Text.Json;
using EchoRankedServerBot.Configuration;
using EchoRankedServerBot.Models.Ip;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EchoRankedServerBot.Services;

public class IpGeolocationService(
    IHttpClientFactory factory,
    IOptions<ApiOptions> apiOptions,
    ILogger<IpGeolocationService> logger)
{
    /// <summary>
    /// Looks up the geographic location of a server IP using ip-api.com.
    /// Returns a string in the format "$region, $city".
    /// </summary>
    public async Task<string> GetServerLocationAsync(string ip)
    {
        var endpoint = $"http://ip-api.com/json/{ip}";
        logger.LogDebug("Requesting server location for IP {Ip}", ip);

        try
        {
            using var client = factory.CreateClient("IpApi");

            var response = await client.GetAsync(endpoint);
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError(
                    "ip-api returned status {StatusCode} for IP {Ip}, so the location is unknown. Response: {Response}",
                    response.StatusCode, ip, Truncate(responseContent));
                return "Unknown Location";
            }

            var ipInfo = JsonSerializer.Deserialize<ServerIpInformation>(responseContent);

            if (ipInfo is null)
            {
                logger.LogWarning(
                    "Failed to deserialize server location data for IP {Ip}. Response: {Response}",
                    ip, Truncate(responseContent));
                return "Unknown Location";
            }

            if (string.IsNullOrWhiteSpace(ipInfo.Region) && string.IsNullOrWhiteSpace(ipInfo.City))
            {
                logger.LogWarning("Region and city fields were both missing for IP {Ip}", ip);
                return "Unknown Location";
            }

            logger.LogDebug("Resolved server location for IP {Ip} to {Region}, {City}", ip, ipInfo.Region, ipInfo.City);
            return $"{ipInfo.Region}, {ipInfo.City}";
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "HttpRequestException while fetching server location for IP {Ip}", ip);
            return "Unknown Location";
        }
        catch (TaskCanceledException ex)
        {
            logger.LogError(ex, "Request timed out while fetching server location for IP {Ip}", ip);
            return "Unknown Location";
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to parse server location response for IP {Ip}", ip);
            return "Unknown Location";
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception while fetching server location for IP {Ip}", ip);
            return "Unknown Location";
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
    /// Checks if the given IP is using a VPN via proxycheck.io.
    /// Returns the VPN operator name or empty string if not a VPN.
    /// </summary>
    public async Task<string> CheckUserForVpnAsync(string ip)
    {
        var apiKey = apiOptions.Value.ProxyCheckApiKey;
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogError("Required configuration value {SettingName} is missing or empty.", "Api:ProxyCheckApiKey");
            return "";
        }

        logger.LogDebug("Checking IP {Ip} for VPN usage", ip);

        try
        {
            using var client = factory.CreateClient("IpApi");

            var response = await client.GetAsync($"http://proxycheck.io/v2/{ip}?key={apiKey}&vpn=1");
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError(
                    "proxycheck.io returned status {StatusCode} for IP {Ip}, so VPN status could not be determined. Response: {Response}",
                    response.StatusCode, ip, Truncate(responseContent));
                return "";
            }

            var vpnApiResponse = JsonSerializer.Deserialize<VpnApiResponse>(responseContent);

            if (vpnApiResponse?.IpDetails is null)
            {
                logger.LogWarning(
                    "VPN response for IP {Ip} was missing the ip details field. Response: {Response}",
                    ip, Truncate(responseContent));
                return "";
            }

            if (vpnApiResponse.IpDetails.Count == 0)
            {
                logger.LogWarning("VPN response for IP {Ip} contained no ip details entries", ip);
                return "";
            }

            foreach (var ipDetails in vpnApiResponse.IpDetails)
            {
                try
                {
                    var rawText = ipDetails.Value.GetRawText();
                    var ipData = JsonSerializer.Deserialize<VpnRoot>(rawText);

                    if (ipData is not { Proxy: "yes", Type: "VPN" }) continue;
                    var vpnOperator = ipData.Operator?.Name ?? "UNKNOWN";
                    logger.LogInformation("VPN detected for IP {Ip}. Operator: {Operator}", ip, vpnOperator);
                    return vpnOperator;
                }
                catch (JsonException ex)
                {
                    logger.LogError(ex, "Failed to parse VPN details for IP {Ip}", ip);
                }
            }

            logger.LogDebug("No VPN detected for IP {Ip}", ip);
        }
        catch (HttpRequestException ex)
        {
            logger.LogError(ex, "HttpRequestException while checking VPN for IP {Ip}", ip);
        }
        catch (TaskCanceledException ex)
        {
            logger.LogError(ex, "Request timed out while checking VPN for IP {Ip}", ip);
        }
        catch (JsonException ex)
        {
            logger.LogError(ex, "Failed to parse VPN response for IP {Ip}", ip);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "General error while checking VPN for IP {Ip}", ip);
        }

        return "";
    }
}
