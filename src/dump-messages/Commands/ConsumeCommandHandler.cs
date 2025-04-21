using System.CommandLine;
using Microsoft.Extensions.Logging;

namespace dump_messages.Commands;

public class ConsumeCommandHandler : ICommandHandler
{
    private readonly ILogger<ConsumeCommandHandler> _logger;

    public ConsumeCommandHandler(ILogger<ConsumeCommandHandler> logger)
    {
        _logger = logger;
    }

    public void Configure(RootCommand rootCommand)
    {
        var consumeCommand = new Command("consume", "Consume messages from a queue");
        
        var consumeQueueOption = new Option<string>("--queue", "Queue name to consume messages from");
        consumeQueueOption.AddAlias("-q");
        consumeQueueOption.IsRequired = true;
            
        consumeCommand.AddOption(consumeQueueOption);
        
        consumeCommand.SetHandler(Handle, consumeQueueOption);
        
        rootCommand.AddCommand(consumeCommand);
    }

    private void Handle(string queue)
    {
        _logger.LogInformation("Consume messages from queue {Queue}", queue);
    }
}
