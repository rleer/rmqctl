using System.Text;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using RmqCli.Configuration;
using RmqCli.Models;
using RmqCli.Utilities;

namespace RmqCli.Services;

public interface IPublishService
{
    Task PublishMessage(Destination dest, List<string> message, int burstCount = 1, CancellationToken cancellationToken = default);
    Task PublishMessageFromFile(Destination dest, FileInfo fileInfo, int burstCount = 1, CancellationToken cancellationToken = default);
}

public class PublishService : IPublishService
{
    private readonly IRabbitChannelFactory _rabbitChannelFactory;
    private readonly ILogger<PublishService> _logger;
    private readonly FileConfig _fileConfig;
    private readonly ICliOutputService _output;

    public PublishService(IRabbitChannelFactory rabbitChannelFactory, ILogger<PublishService> logger, FileConfig fileConfig, ICliOutputService outputService)
    {
        _rabbitChannelFactory = rabbitChannelFactory;
        _logger = logger;
        _fileConfig = fileConfig;
        _output = outputService;
    }

    public async Task PublishMessage(Destination dest, List<string> messages, int burstCount = 1, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "Initiating publish operation: exchange={Exchange}, routing-key={RoutingKey}, queue={Queue}, msg-count={MessageCount}, burst-count={BurstCount}",
            dest.Exchange, dest.RoutingKey, dest.Queue, messages.Count, burstCount);

        await using var channel = await _rabbitChannelFactory.GetChannelWithPublisherConfirmsAsync();

        var totalMessageCount = messages.Count * burstCount;
        var messageCountString = $"[orange1]{totalMessageCount}[/] message{(totalMessageCount > 1 ? "s" : string.Empty)}";
        
        // Prepare the list to collect publish results
        var publishResults = new List<PublishResult>();
        
        try
        {
            // Status output
            _output.ShowStatus($"Publishing {messageCountString} to {GetDestinationString(dest)}...");

            var messageBaseId = GetMessageId();
            for (var m = 0; m < messages.Count; m++)
            {
                var messageIdSuffix = GetMessageIdSuffix(m, messages.Count);
                for (var i = 0; i < burstCount; i++)
                {
                    await Task.Delay(1000);
                    var burstSuffix = burstCount > 1 ? GetMessageIdSuffix(i, burstCount) : string.Empty;
                    var result = await Publish(
                        channel: channel,
                        message: messages[m],
                        messageId: $"{messageBaseId}{messageIdSuffix}{burstSuffix}",
                        exchange: dest.Exchange ?? string.Empty,
                        routingKey: dest.Queue ?? dest.RoutingKey ?? string.Empty,
                        cancellationToken: cancellationToken);
                    publishResults.Add(result);
                }
            }

            _output.ShowSuccess($"Published {messageCountString} successfully");

            _output.WritePublishResult(dest, publishResults);
        }
        catch (AlreadyClosedException ex)
        {
            if (ex.ShutdownReason?.ReplyCode == 404)
            {
                _output.ShowError($"Failed to publish to {GetDestinationString(dest)}: Exchange not found.");
                _logger.LogWarning("Publishing failed with 404 shutdown reason: {Message}", ex.Message);
                return;
            }

            _output.ShowError($"Failed to publish to {GetDestinationString(dest)}", ex.Message);
            _logger.LogWarning(ex, "Publishing failed. Channel was already closed, but not due to a 404 error.");
            throw;
        }
        catch (PublishException ex)
        {
            if (ex.IsReturn)
            {
                _output.ShowError($"Failed to publish to {GetDestinationString(dest)}: No route to destination.");
                _logger.LogDebug(ex, "Caught publish exception due to 'basic.return'");
                return;
            }

            _output.ShowError($"Failed to publish to {GetDestinationString(dest)}", ex.Message);
            _logger.LogError(ex, "Publishing failed but not due to 'basic.return'");
            throw;
        }
        catch (OperationCanceledException)
        {
            _output.ShowWarning("Publishing cancelled by user", addNewLine: true);
            
            var publishCount = publishResults.Count;
            if (publishCount > 0)
            {
                _output.ShowSuccess($"Published [orange1]{publishCount}[/] message{(publishCount > 1 ? "s" : string.Empty)} successfully before cancellation");
                _output.WritePublishResult(dest, publishResults);
            }
            else
            {
                _output.ShowStatus("No messages were published before cancellation");
            }
        }
        finally
        {
            await channel.CloseAsync(cancellationToken: cancellationToken);
            await _rabbitChannelFactory.CloseConnectionAsync();
        }
    }

    public async Task PublishMessageFromFile(Destination dest, FileInfo fileInfo, int burstCount = 1, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Reading message from file: {FilePath}", fileInfo.FullName);
        var messageBlob = await File.ReadAllTextAsync(fileInfo.FullName, cancellationToken);
        var messages = messageBlob
            .Split(_fileConfig.MessageDelimiter)
            .Select(m => m.Trim())
            .Where(m => !string.IsNullOrWhiteSpace(m))
            .ToList();

        var delimiterDisplay = string.Join("", _fileConfig.MessageDelimiter.Select(c => c switch
        {
            '\r' => "\\r",
            '\n' => "\\n",
            '\t' => "\\t",
            _ => c.ToString()
        }));
        _logger.LogDebug("Read {MessageCount} messages: file='{FilePath}', msg-delimiter='{MessageDelimiter}'", messages.Count, fileInfo.FullName,
            delimiterDisplay);

        await PublishMessage(dest, messages, burstCount, cancellationToken);
    }

    private static string GetDestinationString(Destination dest, bool useColor = true)
    {
        var colorPrefix = useColor ? "[orange1]" : string.Empty;
        var colorSuffix = useColor ? "[/]" : string.Empty;
        if (!string.IsNullOrEmpty(dest.Queue))
        {
            return $"queue {colorPrefix}'{dest.Queue}'{colorSuffix}";
        }

        if (!string.IsNullOrEmpty(dest.Exchange) && !string.IsNullOrEmpty(dest.RoutingKey))
        {
            return $"exchange {colorPrefix}'{dest.Exchange}'{colorSuffix} with routing key {colorPrefix}'{dest.RoutingKey}'{colorSuffix}";
        }

        return string.Empty;
    }

    private static async Task<PublishResult> Publish(
        IChannel channel,
        string message,
        string messageId,
        string exchange,
        string routingKey,
        CancellationToken cancellationToken = default)
    {
        var body = Encoding.UTF8.GetBytes(message);
        var props = new BasicProperties
        {
            MessageId = messageId,
            Timestamp = new AmqpTimestamp(DateTimeOffset.Now.ToUnixTimeSeconds())
        };

        await channel.BasicPublishAsync(
            exchange: exchange,
            routingKey: routingKey,
            mandatory: true,
            basicProperties: props,
            body: body,
            cancellationToken: cancellationToken);

        return new PublishResult(props.MessageId, body.LongLength, props.Timestamp);
    }

    /// <summary>
    /// Generates a unique message ID.
    /// </summary>
    /// <example>msg-e3955d32-5461</example>
    /// <returns>Message ID</returns>
    private static string GetMessageId()
    {
        return $"msg-{Guid.NewGuid().ToString("D")[..13]}";
    }
    
    /// <summary>
    /// Generates a suffix for the message ID based on the message index and total messages.
    /// </summary>
    /// <param name="messageIndex"></param>
    /// <param name="totalMessages"></param>
    /// <example>-001</example>
    /// <returns>Message ID suffix</returns>
    private static string GetMessageIdSuffix(int messageIndex, int totalMessages)
    {
        return "-" + $"{messageIndex + 1}".PadLeft(OutputUtilities.GetDigitCount(totalMessages), '0');
    }
}