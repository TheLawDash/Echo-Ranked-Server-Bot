namespace EchoRankedServerBot.Configuration;

public class NakamaOptions
{
    public const string SectionName = "Nakama";

    public string BaseUrl { get; set; } = string.Empty;
    public string AuthEndpoint { get; set; } = string.Empty;
    public string LookupEndpoint { get; set; } = string.Empty;
    public string MatchEndpoint { get; set; } = string.Empty;
    public string PrepareEndpoint { get; set; } = string.Empty;
    public string AssignEndpoint { get; set; } = string.Empty;
    public string StorageEndpoint { get; set; } = string.Empty;
    public string StreamingEndpoint { get; set; } = string.Empty;
    public string ExcludedBroadcasterId { get; set; } = string.Empty;

    // Loaded from environment variables
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string HttpKey { get; set; } = string.Empty;
}
