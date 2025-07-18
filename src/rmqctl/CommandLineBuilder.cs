using System.CommandLine;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using rmqctl.Commands;

namespace rmqctl;

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
        // TODO: handle this option
        var verboseOption = new Option<bool>("--verbose", "Enable verbose logging");
        verboseOption.SetDefaultValue(false);
        _rootCommand.AddGlobalOption(verboseOption);

        // TODO: handle this option
        var configFileOption = new Option<string>("--config", "Path to the configuration file");
        configFileOption.SetDefaultValue("appsettings.json");
        _rootCommand.AddGlobalOption(configFileOption);
    }

    public async Task<int> RunAsync(string[] args)
    {
        return await _rootCommand.InvokeAsync(args);
    }
}