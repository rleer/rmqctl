using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using RmqCli.Configuration;
using RmqCli.Models;
using RmqCli.Utilities;

namespace RmqCli.Services;

public interface IPublishService
{
    Task<int> PublishMessage(Destination dest, List<string> message, int burstCount = 1, CancellationToken cancellationToken = default);
    Task<int> PublishMessageFromFile(Destination dest, FileInfo fileInfo, int burstCount = 1, CancellationToken cancellationToken = default);
}

public partial class PublishService : IPublishService
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

    /// <summary>
    /// Publishes a list of messages to the specified destination.
    /// Supports burst publishing, where each message can be published multiple times.
    /// </summary>
    /// <param name="dest"></param>
    /// <param name="messages"></param>
    /// <param name="burstCount"></param>
    /// <param name="cancellationToken"></param>
    public async Task<int> PublishMessage(Destination dest, List<string> messages, int burstCount = 1, CancellationToken cancellationToken = default)
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
            var errorText = ex.ShutdownReason?.ReplyText ?? ex.Message;

            if (errorText.Contains("not found"))
            {
                _output.ShowError($"Failed to publish to {GetDestinationString(dest)}: Exchange not found.");
                _logger.LogDebug(ex, "Publishing failed with exception");
                return 1;
            }

            if (errorText.Contains("max size"))
            {
                var regex = MaxMessageSizeRegex().Match(errorText);
                var messageSize = regex.Success ? regex.Groups["message_size"].Value : null;
                var maxSize = regex.Success ? regex.Groups["max_size"].Value : null;
                var messageSizeValue = long.TryParse(messageSize, out var size) ? size : 0;
                var maxSizeValue = long.TryParse(maxSize, out var max) ? max : 0;
                var messageSizeString = messageSizeValue > 0 ? $"[orange1]{OutputUtilities.ToSizeString(messageSizeValue)}[/] " : string.Empty;
                var maxSizeString = maxSizeValue > 0 ? $" [orange1]{OutputUtilities.ToSizeString(maxSizeValue)}[/]" : string.Empty;

                _output.ShowError($"Failed to publish to {GetDestinationString(dest)}: " +
                                  $"Message size {messageSizeString}is larger than configured max size{maxSizeString}.");
                _logger.LogDebug(ex, "Publishing failed with exception");
                return 1;
            }

            _output.ShowError($"Failed to publish to {GetDestinationString(dest)}", ex.Message);
            _logger.LogDebug("Publishing failed. Channel was already closed with shutdown reason: {ReplyText} ({ReplyCode})",
                ex.ShutdownReason?.ReplyText, ex.ShutdownReason?.ReplyCode);
            throw;
        }
        catch (PublishException ex)
        {
            if (ex.IsReturn)
            {
                _output.ShowError($"Failed to publish to {GetDestinationString(dest)}: No route to destination.");
                _logger.LogDebug(ex, "Caught publish exception due to 'basic.return'");
                return 1;
            }

            _output.ShowError($"Failed to publish to {GetDestinationString(dest)}", ex.Message);
            _logger.LogDebug("Caught publish exception that is not due to 'basic.return'");
            throw;
        }
        catch (OperationCanceledException)
        {
            _logger.LogDebug("Publishing operation canceled.");
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

        return 0;
    }

    public async Task<int> PublishMessageFromFile(Destination dest, FileInfo fileInfo, int burstCount = 1, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Reading message(s) from file: {FilePath}", fileInfo.FullName);
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
        _logger.LogDebug("Read {MessageCount} message(s): file='{FilePath}', msg-delimiter='{MessageDelimiter}'", messages.Count, fileInfo.FullName,
            delimiterDisplay);

        return await PublishMessage(dest, messages, burstCount, cancellationToken);
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

    [GeneratedRegex(@"message size (?<message_size>\d+).+max size (?<max_size>\d+)$")]
    private static partial Regex MaxMessageSizeRegex();
}