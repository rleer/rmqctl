using System.CommandLine;
using Microsoft.Extensions.Logging;
using rmqctl.Models;
using rmqctl.Services;

namespace rmqctl.Commands;

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
        
        consumeCommand.AddOption(queueOption);
        consumeCommand.AddOption(ackModeOption);
        consumeCommand.AddOption(countOption);
        
        consumeCommand.SetHandler(Handle, queueOption, ackModeOption, countOption);

        rootCommand.AddCommand(consumeCommand);
    }

    private async Task Handle(string queue, AckModes ackMode, int messageCount)
    {
        _logger.LogInformation("Consume {Count} messages from '{Queue}' queue in '{AckMode}' mode",
            messageCount == -1 ? "all" : messageCount.ToString(),queue, ackMode);
        await _consumeService.ConsumeMessages(queue, ackMode, messageCount);
    }
}