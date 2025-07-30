using System.CommandLine;
using Microsoft.Extensions.Logging;
using RmqCli.Models;
using RmqCli.Services;

namespace RmqCli.Commands;

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
            _logger.LogDebug("Configuring publish command");

        var description = """
                          Publish messages to a queue or via exchange and routing-key.
                          
                          Messages can be specified 
                            - directly via the --message option
                            - read from a file using the --from-file option
                            - piped from standard input (STDIN)

                          At the moment, only the message body is supported for publishing. For each message, the following properties are set:
                            - Message ID: auto-generated (incremental)
                            - TimeStamp: current time
                            - Body: the message body specified
                          
                          Example usage:
                            rmq publish --queue my-queue --message "Hello, World!"
                            rmq publish --exchange my-exchange --routing-key my-routing-key --message "Hello, World!"
                            rmq publish --from-file message.txt
                            echo "Hello, World!" | rmq publish --queue my-queue
                          
                          Note that messages are sent with the mandatory flag set to true, meaning that if the message cannot be routed to any queue, 
                          it will be returned to the sender.
                          """;
        
        var publishCommand = new Command("publish", description);

        var queueOption = new Option<string>("--queue", "Queue name to send message to");
        queueOption.AddAlias("-q");

        var exchangeOption = new Option<string>("--exchange", "Exchange name to send message to");
        exchangeOption.AddAlias("-e");

        var routingKeyOption = new Option<string>("--routing-key", "Routing key to send message to");
        routingKeyOption.AddAlias("-r");

        var messageOption = new Option<string>("--message", "Message to send");
        messageOption.AddAlias("-m");

        var fromFileOption = new Option<string>("--from-file", "Path to a file that contains the message body to send");

        var burstOption = new Option<int>("--burst", "Number of messages to send in burst mode");
        burstOption.AddAlias("-b");
        burstOption.SetDefaultValue(1);

        publishCommand.AddOption(queueOption);
        publishCommand.AddOption(exchangeOption);
        publishCommand.AddOption(routingKeyOption);
        publishCommand.AddOption(messageOption);
        publishCommand.AddOption(fromFileOption);
        publishCommand.AddOption(burstOption);

        publishCommand.AddValidator(result =>
        {
            if (result.GetValueForOption(queueOption) is null &&
                (result.GetValueForOption(routingKeyOption) is null || result.GetValueForOption(exchangeOption) is null))
            {
                result.ErrorMessage = "You must specify a queue or both an exchange and a routing key.";
            }

            if (result.GetValueForOption(queueOption) is { } queue && string.IsNullOrEmpty(queue))
            {
                result.ErrorMessage = "Queue name cannot be empty.";
            }

            if (result.GetValueForOption(exchangeOption) is { } exchange && string.IsNullOrEmpty(exchange))
            {
                result.ErrorMessage = "Exchange name cannot be empty. Consider using the --queue option if you want to send messages directly to a queue.";
            }

            if (result.GetValueForOption(routingKeyOption) is { } routingKey && string.IsNullOrEmpty(routingKey))
            {
                result.ErrorMessage = "Routing key cannot be empty. Consider using the --queue option if you want to send messages directly to a queue.";
            }

            if (result.GetValueForOption(messageOption) is null && result.GetValueForOption(fromFileOption) is null && !Console.IsInputRedirected)
            {
                result.ErrorMessage = "You must specify a message using --message, --from-file, or pipe input to STDIN.";
            }

            if (result.GetValueForOption(fromFileOption) is not null && result.GetValueForOption(messageOption) is not null)
            {
                result.ErrorMessage = "You cannot specify both a message and a file that contains the message body.";
            }

            if (result.GetValueForOption(fromFileOption) is { } filePath && !File.Exists(filePath))
            {
                result.ErrorMessage = $"Input file '{filePath}' not found.";
            }
        });

        publishCommand.SetHandler(
            Handle,
            new DestinationBinder(queueOption, exchangeOption, routingKeyOption),
            messageOption,
            fromFileOption,
            burstOption
        );

        rootCommand.AddCommand(publishCommand);
    }

    private async Task<int> Handle(Destination dest, string message, string filePath, int burstCount)
    {
        _logger.LogDebug("Running handler for publish command");

        var cts = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true; // Prevent the process from terminating immediately
            cts.Cancel(); // Signal cancellation
        };

        try
        {
            if (!string.IsNullOrWhiteSpace(filePath))
            {
                var fileInfo = new FileInfo(Path.GetFullPath(filePath, Environment.CurrentDirectory));
                return await _publishService.PublishMessageFromFile(dest, fileInfo, burstCount, cts.Token);
            }

            if (Console.IsInputRedirected)
            {
                return await _publishService.PublishMessageFromStdin(dest, burstCount, cts.Token);
            }

            return await _publishService.PublishMessage(dest, [message], burstCount, cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Cancellation already handled
            return 0;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to publish message");
            return 1;
        }
    }
}