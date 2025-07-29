using System.CommandLine;

namespace RmqCli.Commands;

/// <summary>
/// Interface for command handlers
/// </summary>
public interface ICommandHandler
{
    /// <summary>
    /// Configures the command and adds it to the root command
    /// </summary>
    /// <param name="rootCommand">The root command to add this command to</param>
    void Configure(RootCommand rootCommand);
}