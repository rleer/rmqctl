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
        
        consumeCommand.AddOption(queueOption);
        consumeCommand.AddOption(ackModeOption);
        
        consumeCommand.SetHandler(Handle, queueOption, ackModeOption);

        rootCommand.AddCommand(consumeCommand);
    }

    private async Task Handle(string queue, AckModes ackMode)
    {
        _logger.LogInformation("Consume messages from '{Queue}' queue in '{AckMode}' mode", queue, ackMode);
        await _consumeService.ConsumeMessages(queue, ackMode);
    }
}