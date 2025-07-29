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
    Task PublishMessage(Destination dest, string message, int burstCount = 1, CancellationToken cancellationToken = default);
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

    public async Task PublishMessage(Destination dest, string message, int burstCount = 1, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "Initiating publish operation: exchange={Exchange}, routing-key={RoutingKey}, queue={Queue}, messageLength={Length}, burstCount={Count}",
            dest.Exchange, dest.RoutingKey, dest.Queue, message.Length, burstCount);
        try
        {
            await using var channel = await _rabbitChannelFactory.GetChannelWithPublisherConfirmsAsync();

            AnsiConsole.MarkupLine(
                $"\u26EF Publishing [orange1]{burstCount}[/] message{(burstCount > 1 ? "s" : string.Empty)} to {GetDestinationString(dest, true)}...");

            var messageBaseId = GetMessageId();
            var results = new List<PublishResult>();
            for (int i = 0; i < burstCount; i++)
            {
                var messageId = burstCount > 1 ? "-" + $"{i + 1}".PadLeft(OutputUtilities.GetDigitCount(burstCount), '0') : string.Empty;
                var result = await Publish(
                    channel: channel,
                    message: message,
                    messageId: $"{messageBaseId}{messageId}",
                    exchange: dest.Exchange ?? string.Empty,
                    routingKey: dest.Queue ?? dest.RoutingKey ?? string.Empty,
                    cancellationToken: cancellationToken);
                results.Add(result);
            }

            var messageCountString = burstCount > 1 ? $"[orange1]{burstCount}[/] messages" : "message";
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

            if (burstCount > 1)
            {
                AnsiConsole.MarkupLineInterpolated($"  [dim]Message IDs: {results[0].MessageId} → {results[^1].MessageId}[/]");
                AnsiConsole.MarkupLineInterpolated(
                    $"  [dim]Size:        {results[0].MessageSize} each ({OutputUtilities.ToSizeString(results[0].MessageLength * burstCount)} total)[/]");
                AnsiConsole.MarkupLineInterpolated(
                    $"  [dim]Time:        {results[0].Timestamp:yyyy-MM-dd HH:mm:ss.fff} → {results[^1].Timestamp:yyyy-MM-dd HH:mm:ss.fff}[/]");
            }
            else
            {
                AnsiConsole.MarkupLineInterpolated($"  [dim]Message ID:  {results[0].MessageId}[/]");
                AnsiConsole.MarkupLineInterpolated($"  [dim]Size:        {results[0].MessageSize}[/]");
                AnsiConsole.MarkupLineInterpolated($"  [dim]Time:        {results[0].Timestamp:yyyy-MM-dd HH:mm:ss.fff}[/]");
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
        // TDOO: Adjust publish logic to handle multiple messages in a single file
        // var messages = messageBlob
        //     .Split(_fileConfig.MessageDelimiter)
        //     .Select(m => m.Trim())
        //     .Where(m => !string.IsNullOrWhiteSpace(m))
        //     .ToList();

        await PublishMessage(dest, messageBlob, burstCount, cancellationToken);
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