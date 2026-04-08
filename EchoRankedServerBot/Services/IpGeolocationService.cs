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
        try
        {
            using var client = factory.CreateClient("IpApi");

            var response = await client.GetAsync($"http://ip-api.com/json/{ip}");

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError(
                    "Failed to fetch server location for IP {Ip}. StatusCode: {StatusCode}",
                    ip, response.StatusCode);
                return "Unknown Location";
            }

            var responseContent = await response.Content.ReadAsStringAsync();
            var ipInfo = JsonSerializer.Deserialize<ServerIpInformation>(responseContent);

            if (ipInfo is not null) return $"{ipInfo.Region}, {ipInfo.City}";
            logger.LogError(
                "Failed to deserialize server location data for IP {Ip}. Response: {Response}",
                ip, responseContent);
            return "Unknown Location";

        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Exception while fetching server location for IP {Ip}", ip);
            return "Unknown Location";
        }
    }

    /// <summary>
    /// Checks if the given IP is using a VPN via proxycheck.io.
    /// Returns the VPN operator name or empty string if not a VPN.
    /// </summary>
    public async Task<string> CheckUserForVpnAsync(string ip)
    {
        try
        {
            using var client = factory.CreateClient("IpApi");

            var apiKey = apiOptions.Value.ProxyCheckApiKey;
            var response = await client.GetAsync($"http://proxycheck.io/v2/{ip}?key={apiKey}&vpn=1");
            var responseContent = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError(
                    "Failed to fetch VPN details for IP {Ip}. StatusCode: {StatusCode}, Response: {Response}",
                    ip, response.StatusCode, responseContent);
                return "";
            }

            var vpnApiResponse = JsonSerializer.Deserialize<VpnApiResponse>(responseContent);

            if (vpnApiResponse?.IpDetails is null)
            {
                logger.LogError(
                    "Failed to deserialize VPN response for IP {Ip}. Response: {Response}",
                    ip, responseContent);
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
                    return vpnOperator;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Error processing VPN data for IP {Ip}", ip);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "General error while checking VPN for IP {Ip}", ip);
        }

        return "";
    }
}
