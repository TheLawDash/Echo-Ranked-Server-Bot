using System.Text.Json;
using EchoRankedServerBot.Models.Config;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace EchoRankedServerBot.Services;

public class BotConfigService(ILogger<BotConfigService> logger, IConfiguration configuration)
{
    private readonly string _configPath = configuration["Bot:RuntimeConfigPath"] ?? "bot-config.json";

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
                if (File.Exists(_configPath))
                {
                    var json = File.ReadAllText(_configPath);
                    var deserialized = JsonSerializer.Deserialize<BotConfig>(json);
                    if (deserialized is null)
                    {
                        logger.LogWarning(
                            "The bot config file at {ConfigPath} was empty or invalid, so default settings were used.",
                            _configPath);
                    }

                    _cachedConfig = deserialized ?? new BotConfig();
                }
                else
                {
                    _cachedConfig = new BotConfig();
                    SaveConfigInternal(_cachedConfig);
                    logger.LogInformation(
                        "No bot config file was found, so a default config file was created at {ConfigPath}.",
                        _configPath);
                }

                return _cachedConfig;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to load bot config from {ConfigPath}", _configPath);
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
            logger.LogInformation("Setting {SettingName} changed to {SettingValue}",
                nameof(BotConfig.Enforce1000MmrPartyRestriction), enabled);
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
            File.WriteAllText(_configPath, json);
            _cachedConfig = config;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Could not save the bot config to {ConfigPath}, so the change may not persist across restarts.", _configPath);
        }
    }
}
