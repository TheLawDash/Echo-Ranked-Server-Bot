using EchoRankedServerBot.Configuration;
using Microsoft.Extensions.Configuration;

namespace EchoRankedServerBot.Extensions;

public static class ConfigurationExtensions
{
    private static IConfiguration _configuration = null!;

    /// <summary>
    /// Environment variables that the bot cannot start or operate correctly without.
    /// </summary>
    private static readonly HashSet<string> RequiredVariables =
    [
        DataConstants.EnvironmentVariables.EchoRankedDiscordToken,
        DataConstants.EnvironmentVariables.EchoRankedPostgresConnection,
        DataConstants.EnvironmentVariables.EchoRankedNakamaUsername,
        DataConstants.EnvironmentVariables.EchoRankedNakamaPassword,
        DataConstants.EnvironmentVariables.EchoRankedNakamaHttpKey
    ];

    public static void Initialize(IConfiguration configuration) => _configuration = configuration;

    /// <summary>
    /// Reads a value from the environment variables or configuration. Required values throw a
    /// clear, human readable exception when missing so the bot fails fast at startup instead of
    /// failing later with a confusing error. Optional values (such as the NeatQueue api key) return
    /// null or empty silently.
    /// </summary>
    public static string GetAsEnvironmentVariable(this string key)
    {
        var value = Environment.GetEnvironmentVariable(key) ?? _configuration[key];

        if (!string.IsNullOrEmpty(value))
        {
            return value;
        }

        if (RequiredVariables.Contains(key))
        {
            throw new InvalidOperationException(
                $"The environment variable {key} is not set. The bot cannot start without it.");
        }

        return value ?? string.Empty;
    }
}
