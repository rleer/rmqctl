using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RmqCli.Commands;

namespace RmqCli;

public class CommandLineBuilder
{
    private readonly IHost _host;
    private readonly RootCommand _rootCommand;

    private const string RabbitAscii = """
                                         (\(\
                                         (-.-)
                                         o(")(")
                                       """;

    public CommandLineBuilder(IHost host)
    {
        _host = host;
        _rootCommand = new RootCommand($"{RabbitAscii}\nDeveloper focused utility tool for common RabbitMQ tasks");
    }

    public void ConfigureCommands()
    {
        ConfigureGlobalOptions();

        var commands = _host.Services.GetServices<ICommandHandler>();
        foreach (var command in commands)
        {
            command.Configure(_rootCommand);
        }
    }

    private void ConfigureGlobalOptions()
    {
        // Global options are parsed first in Program.cs to set up the environment and also need to be specified here
        // for help text and property validation.
        var verboseOption = new Option<bool>("--verbose", "Enable verbose logging");
        verboseOption.SetDefaultValue(false);
        _rootCommand.AddGlobalOption(verboseOption);

        var quietOption = new Option<bool>("--quiet", "Minimal output (errors only)");
        quietOption.SetDefaultValue(false);
        _rootCommand.AddGlobalOption(quietOption);

        var jsonOption = new Option<bool>("--json", "Structured JSON output to stdout");
        jsonOption.SetDefaultValue(false);
        _rootCommand.AddGlobalOption(jsonOption);

        var noColorOption = new Option<bool>("--no-color", "Disable colored output for dumb terminals");
        noColorOption.SetDefaultValue(false);
        _rootCommand.AddGlobalOption(noColorOption);

        var configFileOption = new Option<string>("--config", "Path to the configuration file (TOML format)");
        _rootCommand.AddGlobalOption(configFileOption);
        
        _rootCommand.AddValidator(result =>
        {
            if (result.GetValueForOption(verboseOption) && result.GetValueForOption(quietOption))
            {
                result.ErrorMessage = "You cannot use both --verbose and --quiet options together.";
            }
        });
    }

    public async Task<int> RunAsync(string[] args)
    {
        return await _rootCommand.InvokeAsync(args);
    }
}