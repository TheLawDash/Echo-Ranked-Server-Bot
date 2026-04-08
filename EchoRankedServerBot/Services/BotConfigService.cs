using System.Text.Json;
using EchoRankedServerBot.Models.Config;
using Microsoft.Extensions.Logging;

namespace EchoRankedServerBot.Services;

public class BotConfigService(ILogger<BotConfigService> logger)
{
    private const string ConfigPath = "bot-config.json";

    private static readonly JsonSerializerOptions WriteOptions = new() { WriteIndented = true };

    private readonly Lock _lock = new();
    private BotConfig? _cachedConfig;

    /// <summary>
    /// Loads the bot configuration from disk. Creates a default config file if none exists.
    /// </summary>
    private BotConfig LoadConfig()
    {
        lock (_lock)
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath);
                    _cachedConfig = JsonSerializer.Deserialize<BotConfig>(json) ?? new BotConfig();
                }
                else
                {
                    _cachedConfig = new BotConfig();
                    SaveConfigInternal(_cachedConfig);
                }

                return _cachedConfig;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to load bot config from {ConfigPath}", ConfigPath);
                return new BotConfig();
            }
        }
    }

    /// <summary>
    /// Sets the Enforce1000MmrPartyRestriction flag and saves to disk.
    /// </summary>
    public void SetEnforce1000MmrPartyRestriction(bool enabled)
    {
        lock (_lock)
        {
            var config = GetConfigInternal();
            config.Enforce1000MmrPartyRestriction = enabled;
            SaveConfigInternal(config);
        }
    }

    /// <summary>
    /// Returns whether the Enforce1000MmrPartyRestriction setting is enabled.
    /// </summary>
    public bool IsEnforce1000MmrPartyRestrictionEnabled()
    {
        lock (_lock)
        {
            return GetConfigInternal().Enforce1000MmrPartyRestriction;
        }
    }

    private BotConfig GetConfigInternal()
    {
        _cachedConfig ??= LoadConfig();
        return _cachedConfig;
    }

    private void SaveConfigInternal(BotConfig config)
    {
        try
        {
            var json = JsonSerializer.Serialize(config, WriteOptions);
            File.WriteAllText(ConfigPath, json);
            _cachedConfig = config;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to save bot config to {ConfigPath}", ConfigPath);
        }
    }
}
