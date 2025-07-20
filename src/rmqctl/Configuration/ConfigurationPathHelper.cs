namespace rmqctl.Configuration;

public class ConfigurationPathHelper
{
    private static string GetUserConfigDirectory()
    {
        var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        return Environment.OSVersion.Platform switch
        {
            PlatformID.Unix or PlatformID.MacOSX => Path.Combine(homeDir, ".config", Constants.AppName),
            _ => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Constants.AppName)
        };
    }

    public static string GetUserConfigFilePath()
    {
        return Path.Combine(GetUserConfigDirectory(), "config.toml");
    }

    public static string GetSystemConfigFilePath()
    {
        return Environment.OSVersion.Platform switch
        {
            PlatformID.Unix or PlatformID.MacOSX => $"/etc/{Constants.AppName}/config.toml",
            _ => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), Constants.AppName, "config.toml")
        };
    }

    private static void EnsureUserConfigDirectoryExists()
    {
        var configDir = GetUserConfigDirectory();
        if (!Directory.Exists(configDir))
        {
            Directory.CreateDirectory(configDir);
        }
    }

    public static void CreateDefaultUserConfigIfNotExists()
    {
        var configPath = GetUserConfigFilePath();
        if (!File.Exists(configPath))
        {
            EnsureUserConfigDirectoryExists();

            var defaultConfig = GenerateDefaultTomlConfig();
            File.WriteAllText(configPath, defaultConfig);
        }
    }

    private static string GenerateDefaultTomlConfig()
    {
        return """
               # rmqctl Configuration File
               # This file contains default settings for the rmqctl CLI tool

               [RabbitMqConfig]
               Host = "localhost"
               Port = 5672
               VirtualHost = "/"
               User = "guest"
               Password = "guest"
               Exchange = "amq.direct"
               ClientName = "rabbit-cli-tool"
               PrefetchCount = 200

               [FileConfig]
               MessageDelimiter = "=================="
               MessagesPerFile = 10000

               [Logging.LogLevel]
               Default = "Information"
               Microsoft = "Warning"
               "Microsoft.Hosting.Lifetime" = "Information"
               """;
    }
}