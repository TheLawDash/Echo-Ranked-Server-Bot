namespace EchoRankedServerBot.Configuration;

public class ApiOptions
{
    public const string SectionName = "Api";

    public string IpApiBaseUrl { get; set; } = string.Empty;
    public string NeatQueueBaseUrl { get; set; } = string.Empty;
    public string ProxyCheckBaseUrl { get; set; } = string.Empty;
    public string ProxyCheckApiKey { get; set; } = string.Empty;
    public string EchoTaxiBaseUrl { get; set; } = string.Empty;
}
