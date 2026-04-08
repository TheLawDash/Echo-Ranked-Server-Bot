using Microsoft.Extensions.Configuration;

namespace EchoRankedServerBot.Extensions;

public static class ConfigurationExtensions
{
    private static IConfiguration _configuration = null!;

    public static void Initialize(IConfiguration configuration) => _configuration = configuration;

    public static string GetAsEnvironmentVariable(this string key)
    {
        return Environment.GetEnvironmentVariable(key)
               ?? _configuration[key]
               ?? throw new InvalidOperationException(
                   $"'{key}' is not set as an environment variable or in appsettings.json.");
    }
}
