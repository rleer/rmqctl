using System.CommandLine;
using Microsoft.Extensions.Logging;
using RmqCli.Models;
using RmqCli.Services;
using RmqCli.Utilities;

namespace RmqCli.Commands;

public class ConsumeCommandHandler : ICommandHandler
{
    private readonly ILogger<ConsumeCommandHandler> _logger;
    private readonly IConsumeService _consumeService;

    public ConsumeCommandHandler(ILogger<ConsumeCommandHandler> logger, IConsumeService consumeService)
    {
        _logger = logger;
        _consumeService = consumeService;
    }

    public void Configure(RootCommand rootCommand)
    {
        _logger.LogDebug("Configuring consume command...");

        var consumeCommand = new Command("consume", "Consume messages from a queue. Warning: getting messages from a queue is a destructive action!");

        var queueOption = new Option<string>("--queue", "Queue name to consume messages from");
        queueOption.AddAlias("-q");
        queueOption.IsRequired = true;

        var ackModeOption = new Option<AckModes>("--ack-mode", "Acknowledgment mode");
        ackModeOption.AddAlias("-a");
        ackModeOption.SetDefaultValue(AckModes.Ack);

        var countOption = new Option<int>("--count", "Number of messages to consume (-1 for all messages)");
        countOption.AddAlias("-c");
        countOption.SetDefaultValue(-1);

        var outputFileOption = new Option<string>("--to-file", "Output file to write messages to. Or just pipe/redirect output to a file.");

        var outputFormatOption = new Option<OutputFormat>("--output", "Output format. One of: plain, table or json.");
        outputFormatOption.AddAlias("-o");
        outputFormatOption.SetDefaultValue(OutputFormat.Plain);
        
        consumeCommand.AddOption(queueOption);
        consumeCommand.AddOption(ackModeOption);
        consumeCommand.AddOption(countOption);
        consumeCommand.AddOption(outputFileOption);
        consumeCommand.AddOption(outputFormatOption);

        consumeCommand.AddValidator(result =>
        {
            if (result.GetValueForOption(queueOption) is null)
            {
                result.ErrorMessage = "You must specify a queue to consume messages from.";
            }

            if (result.GetValueForOption(outputFileOption) is { } filePath && !PathValidator.IsValidFilePath(filePath))
            {
                result.ErrorMessage = $"The specified output file '{filePath}' is not valid.";
            }

            if (result.GetValueForOption(countOption) is 0)
            {
                result.ErrorMessage = "If you don't want to consume any message, don't bother to run this command.";
            }
        });

        consumeCommand.SetHandler(Handle, queueOption, ackModeOption, countOption, outputFileOption, outputFormatOption);

        rootCommand.AddCommand(consumeCommand);
    }

    private async Task Handle(string queue, AckModes ackMode, int messageCount, string outputFilePath, OutputFormat outputFormat)
    {
        _logger.LogDebug("[*] Running handler for consume command...");
        
        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (sender, e) =>
        {
            e.Cancel = true; // Prevent the process from terminating immediately
            cts.Cancel();    // Signal cancellation
        };
        _logger.LogInformation("[*] Press Ctrl+C to stop consuming messages...");

        FileInfo? outputFileInfo = null;
        if (!string.IsNullOrWhiteSpace(outputFilePath))
        {
            outputFileInfo = new FileInfo(Path.GetFullPath(outputFilePath, Environment.CurrentDirectory));
        }

        await _consumeService.ConsumeMessages(queue, ackMode, outputFileInfo, messageCount, outputFormat, cts.Token); 

        _logger.LogDebug("[x] Message consumer is done (cts: {CancellationToken}). Stopping application...", cts.IsCancellationRequested);
    }
}