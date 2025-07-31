using System.CommandLine;
using RmqCli.Configuration;

namespace RmqCli.Commands;

public class ConfigCommandHandler : ICommandHandler
{
    public void Configure(RootCommand rootCommand)
    {
        const string description = """
                                   Manage configuration files for the rmq CLI tool.

                                   This command allows you to view, initialize, edit, and reset the configuration files used by RmqCli.

                                   On the first run, a default configuration file is created in the user's home directory.
                                   The configuration file is in TOML format. All available options are documented in the default configuration file.

                                   Example usage:
                                     rmq config show
                                     rmq config init
                                     rmq config path
                                     rmq config edit
                                     rmq config reset
                                   """;
        
        var configCommand = new Command("config", description);

        // config show - shows the current configuration file path and contents
        var showCommand = new Command("show", "Show current configuration");
        showCommand.SetHandler(ShowConfig);
        configCommand.AddCommand(showCommand);

        // config init - initializes a default configuration file
        var initCommand = new Command("init", "Initialize a default configuration file");
        initCommand.SetHandler(InitConfig);
        configCommand.AddCommand(initCommand);

        // config path - shows the path to the current configuration file
        var pathCommand = new Command("path", "Show the path to the current configuration file");
        pathCommand.SetHandler(ShowConfigPath);
        configCommand.AddCommand(pathCommand);

        // config edit - opens the configuration file in the default editor
        var editCommand = new Command("edit", "Edit the configuration file in the default editor");
        editCommand.SetHandler(EditConfig);
        configCommand.AddCommand(editCommand);

        // config reset - resets the configuration file to default
        var resetCommand = new Command("reset", "Reset the configuration file to default");
        resetCommand.SetHandler(ResetConfig);
        configCommand.AddCommand(resetCommand);

        rootCommand.AddCommand(configCommand);
    }

    private static void ShowConfig()
    {
        var userConfigPath = TomlConfigurationHelper.GetUserConfigFilePath();
        var systemConfigPath = TomlConfigurationHelper.GetSystemConfigFilePath();
        var configFound = false;

        if (File.Exists(userConfigPath))
        {
            Console.WriteLine($"Current user configuration file: {userConfigPath}");
            Console.WriteLine();
            var userConfig = File.ReadAllText(userConfigPath);
            Console.WriteLine(userConfig);
            configFound = true;
        }

        if (File.Exists(systemConfigPath))
        {
            Console.WriteLine($"Current system-wide configuration file: {systemConfigPath}");
            Console.WriteLine();
            var systemConfig = File.ReadAllText(systemConfigPath);
            Console.WriteLine(systemConfig);
            configFound = true;
        }

        if (!configFound)
        {
            Console.Error.WriteLine("No configuration file found. Run the 'config init' command to create a default configuration file.");
        }
    }

    private static void InitConfig()
    {
        TomlConfigurationHelper.CreateDefaultUserConfigIfNotExists();
    }

    private static void ShowConfigPath()
    {
        if (File.Exists(TomlConfigurationHelper.GetUserConfigFilePath()))
        {
            Console.WriteLine($"User configuration file path: {TomlConfigurationHelper.GetUserConfigFilePath()}");
        }
        else if (File.Exists(TomlConfigurationHelper.GetSystemConfigFilePath()))
        {
            Console.WriteLine($"System-wide configuration file path: {TomlConfigurationHelper.GetSystemConfigFilePath()}");
        }
        else
        {
            Console.WriteLine("No configuration file found. Run the 'config init' command to create a default configuration file.");
        }
    }

    private static void EditConfig()
    {
        var configPath = TomlConfigurationHelper.GetUserConfigFilePath();
        if (!File.Exists(configPath))
        {
            Console.Error.WriteLine("Configuration file does not exist. Run 'config init' to create a default configuration file.");
            return;
        }

        try
        {
            // Open the configuration file in the default editor
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = configPath,
                UseShellExecute = true
            });
            Console.WriteLine($"Opened configuration file in the default editor: {configPath}");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Failed to open configuration file: {ex.Message}");
            Console.Error.WriteLine($"Please manually edit the configuration file at: {configPath}");
        }
    }

    private static void ResetConfig()
    {
        var configPath = TomlConfigurationHelper.GetUserConfigFilePath();
        if (File.Exists(configPath))
        {
            try
            {
                File.Delete(configPath);
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"Failed to delete previous configuration file: {ex.Message}");
            }
        }
        
        TomlConfigurationHelper.CreateDefaultUserConfigIfNotExists();
        Console.WriteLine($"Configuration reset to defaults: {configPath}");
    }
}