using System.CommandLine;
using Microsoft.Extensions.Logging;
using rmqctl.Models;
using rmqctl.Services;

namespace rmqctl.Commands;

public class PublishCommandHandler : ICommandHandler
{
    private readonly IPublishService _publishService;
    private readonly ILogger<PublishCommandHandler> _logger;

    public PublishCommandHandler(IPublishService publishService, ILogger<PublishCommandHandler> logger)
    {
        _publishService = publishService;
        _logger = logger;
    }

    public void Configure(RootCommand rootCommand)
    {
        _logger.LogDebug("Configuring publish command...");
        
        var publishCommand = new Command("publish", "Publish message to a queue");
        
        var queueOption = new Option<string>("--queue", "Queue name to send message to");
        queueOption.AddAlias("-q");
        
        var exchangeOption = new Option<string>("--exchange", "Exchange name to send message to");
        exchangeOption.AddAlias("-e");
        exchangeOption.SetDefaultValue("amq.direct");
        
        var routingKeyOption = new Option<string>("--routing-key", "Routing key to send message to");
        routingKeyOption.AddAlias("-r");

        var messageOption = new Option<string>("--message", "Message to send");
        messageOption.AddAlias("-m");
            
        var fromFileOption = new Option<string>("--from-file", "Path to a file that contains the message body to send");
        
        publishCommand.AddOption(queueOption);
        publishCommand.AddOption(exchangeOption);
        publishCommand.AddOption(routingKeyOption);
        publishCommand.AddOption(messageOption);
        publishCommand.AddOption(fromFileOption);
        
        publishCommand.AddValidator(result =>
        {
            if (result.GetValueForOption(queueOption) is null && result.GetValueForOption(routingKeyOption) is null)
            {
                result.ErrorMessage = "You must specify either a queue or an exchange and routing key.";
            }

            if (result.GetValueForOption(messageOption) is null && result.GetValueForOption(fromFileOption) is null)
            {
                result.ErrorMessage = "You must specify a message to send or a file that contains the message body.";
            }
            if (result.GetValueForOption(fromFileOption) is not null && result.GetValueForOption(messageOption) is not null)
            {
                result.ErrorMessage = "You cannot specify both a message and a file that contains the message body.";
            }
        });
        publishCommand.SetHandler(Handle, new DestinationBinder(queueOption, exchangeOption, routingKeyOption), messageOption, fromFileOption);
        
        rootCommand.AddCommand(publishCommand);
    }

    private async Task Handle(Destination dest, string message, string filePath)
    {
        _logger.LogDebug("Running handler for publish command...");
        if (!string.IsNullOrEmpty(filePath))
        {
            var fileInfo = new FileInfo(Path.GetFullPath(filePath, Environment.CurrentDirectory));
            await _publishService.PublishMessageFromFile(dest, fileInfo);
        }
        else
        {
            await _publishService.PublishMessage(dest, message);
        }
    }
}