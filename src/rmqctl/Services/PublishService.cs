using System.Text;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using rmqctl.Configuration;
using rmqctl.Models;
using rmqctl.Utilities;
using Spectre.Console;

namespace rmqctl.Services;

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

    public PublishService(IRabbitChannelFactory rabbitChannelFactory, ILogger<PublishService> logger, FileConfig fileConfig)
    {
        _rabbitChannelFactory = rabbitChannelFactory;
        _logger = logger;
        _fileConfig = fileConfig;
    }

    public async Task PublishMessage(Destination dest, List<string> messages, int burstCount = 1, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "Initiating publish operation: exchange={Exchange}, routing-key={RoutingKey}, queue={Queue}, msg-count={MessageCount}, burst-count={BurstCount}",
            dest.Exchange, dest.RoutingKey, dest.Queue, messages.Count, burstCount);
        try
        {
            await using var channel = await _rabbitChannelFactory.GetChannelWithPublisherConfirmsAsync();

            var totalMessageCount = messages.Count * burstCount;
            var messageCountString = $"[orange1]{totalMessageCount}[/] message{(totalMessageCount > 1 ? "s" : string.Empty)}";

            AnsiConsole.MarkupLine($"\u26EF Publishing {messageCountString} to {GetDestinationString(dest, true)}...");

            var publishResults = new List<PublishResult>();

            var messageBaseId = GetMessageId();
            for (var m = 0; m < messages.Count; m++)
            {
                var messageIdSuffix = "-" + $"{m + 1}".PadLeft(OutputUtilities.GetDigitCount(messages.Count), '0');
                for (var i = 0; i < burstCount; i++)
                {
                    var burstSuffix = burstCount > 1 ? "-" + $"{i + 1}".PadLeft(OutputUtilities.GetDigitCount(burstCount), '0') : string.Empty;
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

            AnsiConsole.MarkupLine($"✓ Published {messageCountString} successfully");

            if (dest.Queue is not null)
            {
                AnsiConsole.MarkupLineInterpolated($"  [dim]Queue:       {dest.Queue}[/]");
            }
            else if (dest is { Exchange: not null, RoutingKey: not null })
            {
                AnsiConsole.MarkupLineInterpolated($"  [dim]Exchange:    {dest.Exchange}[/]");
                AnsiConsole.MarkupLineInterpolated($"  [dim]Routing Key: {dest.RoutingKey}[/]");
            }

            if (totalMessageCount > 1)
            {
                AnsiConsole.MarkupLineInterpolated($"  [dim]Message IDs: {publishResults[0].MessageId} → {publishResults[^1].MessageId}[/]");

                var avgSize = Math.Round(publishResults.Sum(m => m.MessageLength) / (double)totalMessageCount, 2);
                var avgSizeString = OutputUtilities.ToSizeString(avgSize);
                var totalSizeString = OutputUtilities.ToSizeString(publishResults.Sum(m => m.MessageLength));

                AnsiConsole.MarkupLineInterpolated($"  [dim]Size:        {avgSizeString} avg. ({totalSizeString} total)[/]");
                AnsiConsole.MarkupLineInterpolated($"  [dim]Time:        {publishResults[0].Timestamp:yyyy-MM-dd HH:mm:ss.fff} → {publishResults[^1].Timestamp:yyyy-MM-dd HH:mm:ss.fff}[/]");
            }
            else
            {
                AnsiConsole.MarkupLineInterpolated($"  [dim]Message ID:  {publishResults[0].MessageId}[/]");
                AnsiConsole.MarkupLineInterpolated($"  [dim]Size:        {publishResults[0].MessageSize}[/]");
                AnsiConsole.MarkupLineInterpolated($"  [dim]Time:        {publishResults[0].Timestamp:yyyy-MM-dd HH:mm:ss.fff}[/]");
            }
        }
        catch (AlreadyClosedException ex)
        {
            if (ex.ShutdownReason?.ReplyCode == 404)
            {
                AnsiConsole.MarkupLine($"✗ Failed to publish to {GetDestinationString(dest, true)}: Exchange not found.");
                _logger.LogWarning("Publishing failed with 404 shutdown reason: {Message}", ex.Message);
                return;
            }

            _logger.LogWarning(ex, "Publishing failed. Channel was already closed, but not due to a 404 error.");
            throw;
        }
        catch (PublishException ex)
        {
            if (ex.IsReturn)
            {
                AnsiConsole.MarkupLine($"✗ Failed to publish to {GetDestinationString(dest, true)}: No route to destination.");
                _logger.LogDebug(ex, "Caught publish exception due to 'basic.return'");
                return;
            }

            _logger.LogError(ex, "Publishing failed but no due to 'basic.return'");
            throw;
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
        _logger.LogDebug("Read {MessageCount} messages: file='{FilePath}', msg-delimiter='{MessageDelimiter}'", messages.Count, fileInfo.FullName, delimiterDisplay);

        await PublishMessage(dest, messages, burstCount, cancellationToken);
    }

    private static string GetDestinationString(Destination dest, bool useColor = false)
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
    /// <returns></returns>
    private static string GetMessageId()
    {
        return $"msg-{Guid.NewGuid().ToString("D")[..13]}";
    }

    private record PublishResult(
        string MessageId,
        long MessageLength,
        AmqpTimestamp AmqTime)
    {
        public string MessageSize => OutputUtilities.ToSizeString(MessageLength);
        public DateTimeOffset Timestamp => DateTimeOffset.FromUnixTimeSeconds(AmqTime.UnixTime);
    }
}