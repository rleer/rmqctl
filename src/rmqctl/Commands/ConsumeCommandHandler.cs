using System.CommandLine;
using Microsoft.Extensions.Logging;
using rmqctl.Configuration;
using rmqctl.Models;
using rmqctl.Services;
using rmqctl.Utilities;

namespace rmqctl.Commands;

public class ConsumeCommandHandler : ICommandHandler
{
    private readonly ILogger<ConsumeCommandHandler> _logger;
    private readonly IConsumeService _consumeService;
    private readonly DaemonConfig _daemonConfig;

    public ConsumeCommandHandler(ILogger<ConsumeCommandHandler> logger, IConsumeService consumeService, DaemonConfig daemonConfig)
    {
        _logger = logger;
        _consumeService = consumeService;
        _daemonConfig = daemonConfig;
    }

    public void Configure(RootCommand rootCommand)
    {
        _logger.LogDebug("Configuring consume command...");

        var consumeCommand = new Command("consume", "Consume messages from a queue");

        var queueOption = new Option<string>("--queue", "Queue name to consume messages from");
        queueOption.AddAlias("-q");
        queueOption.IsRequired = true;

        var ackModeOption = new Option<AckModes>("--ack-mode", "Acknowledgment mode");
        ackModeOption.AddAlias("-a");
        ackModeOption.SetDefaultValue(AckModes.Ack);

        var countOption = new Option<int>("--count", "Number of messages to consume (-1 for all messages)");
        countOption.AddAlias("-c");
        countOption.SetDefaultValue(-1);

        var daemonOption = new Option<bool>("--daemon", "Run as a daemon, consuming messages continuously until stopped or count is reached");
        daemonOption.AddAlias("-d");
        daemonOption.SetDefaultValue(false);

        var outputOption = new Option<string>("--output", "Output file to write messages to");

        consumeCommand.AddOption(queueOption);
        consumeCommand.AddOption(ackModeOption);
        consumeCommand.AddOption(countOption);
        consumeCommand.AddOption(outputOption);
        consumeCommand.AddOption(daemonOption);

        consumeCommand.AddValidator(result =>
        {
            if (result.GetValueForOption(queueOption) is null)
            {
                result.ErrorMessage = "You must specify a queue to consume messages from.";
            }

            if (result.GetValueForOption(outputOption) is { } filePath && !PathValidator.IsValidFilePath(filePath))
            {
                result.ErrorMessage = $"The specified output file '{filePath}' is not valid.";
            }

            if (result.GetValueForOption(countOption) is 0)
            {
                result.ErrorMessage = "If you don't want to consume any message, don't bother to run this command.";
            }
        });

        consumeCommand.SetHandler(Handle, queueOption, ackModeOption, countOption, outputOption, daemonOption);

        rootCommand.AddCommand(consumeCommand);
    }

    private async Task Handle(string queue, AckModes ackMode, int messageCount, string outputFilePath, bool daemon)
    {
        _logger.LogDebug("Running handler for consume command...");

        if (daemon)
        {
            // Set the daemon configuration and return
            // The host will continue running with the background consumer service
            _daemonConfig.IsDaemonMode = true;
            _daemonConfig.Queue = queue;
            _daemonConfig.AckMode = ackMode;
            _daemonConfig.MessageCount = messageCount;
            return;
        }

        // Continue in non-daemon mode
        if (string.IsNullOrWhiteSpace(outputFilePath))
        {
            await _consumeService.ConsumeMessages(queue, ackMode, messageCount);
        }
        else
        {
            var outputFileInfo = new FileInfo(Path.GetFullPath(outputFilePath, Environment.CurrentDirectory));
            await _consumeService.DumpMessagesToFile(queue, ackMode, outputFileInfo, messageCount);
        }
    }
}