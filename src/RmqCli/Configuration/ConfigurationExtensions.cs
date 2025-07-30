using Microsoft.Extensions.Configuration;

namespace RmqCli.Configuration;

public static class ConfigurationExtensions
{
    public static IConfigurationBuilder AddRmqConfig(this IConfigurationBuilder builder, string? customConfigPath)
    {
        // Build configuration in priority order (lowest to highest priority):
        // 1. Default TOML config (fallback)
        // 2. System-wide TOML config
        // 3. User TOML config
        // 4. Custom TOML config (if specified via --config)
        // 5. Environment variables
        // Create default user config if it does not exist
        ConfigurationPathHelper.CreateDefaultUserConfigIfNotExists();

        // Add TOML configuration sources in priority order
        var systemConfigPath = ConfigurationPathHelper.GetSystemConfigFilePath();
        if (File.Exists(systemConfigPath))
        {
            builder.AddTomlConfig(systemConfigPath);
        }

        var userConfigPath = ConfigurationPathHelper.GetUserConfigFilePath();
        if (File.Exists(userConfigPath))
        {
            builder.AddTomlConfig(userConfigPath);
        }

        // Add custom configuration file if specified
        if (File.Exists(customConfigPath))
        {
            builder.AddTomlConfig(customConfigPath);
        }
        else if (!string.IsNullOrEmpty(customConfigPath))
        {
            // TODO: User logger and add user-friendly error message
            Console.Error.WriteLine("⚠ Configuration file not found. Using default configuration.");
        }

        // Add environment variables as the highest priority configuration source
        builder.AddEnvironmentVariables("RMQCLI_");

        return builder;
    }
}