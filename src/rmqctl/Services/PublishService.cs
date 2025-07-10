using System.Text;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using rmqctl.Models;

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

    public PublishService(IRabbitChannelFactory rabbitChannelFactory, ILogger<PublishService> logger)
    {
        _rabbitChannelFactory = rabbitChannelFactory;
        _logger = logger;
    }

    public async Task PublishMessage(Destination dest, string message, int burstCount = 1, CancellationToken cancellationToken = default)
    {
        try
        {
            await using var channel = await _rabbitChannelFactory.GetChannelAsync();

            if (dest.Queue is not null)
            {
                await PublishViaTempKey(channel, dest.Queue, message, dest.Exchange, burstCount, cancellationToken);
            }
            else if (dest.Exchange is not null && dest.RoutingKey is not null)
            {
                _logger.LogInformation("[*] Publishing {Count} {Messages} via exchange: {Exchange}, routing-key: {RoutingKey}...",
                    burstCount, burstCount > 1 ? "messages" : "message", dest.Exchange, dest.RoutingKey);
                await Publish(channel, message, dest.Exchange, dest.RoutingKey, burstCount, cancellationToken);
            }
            else
            {
                throw new ArgumentException("Either a queue or a combination of exchange and routing key must be specified");
            }
        }
        catch (OperationInterruptedException e)
        {
            _logger.LogError("[x] Failed to publish message with target {Destination}: {Message}",
                dest.Queue ?? $"{dest.RoutingKey} via {dest.Exchange}",
                e.Message);
        }
    }

    public Task PublishMessageFromFile(Destination dest, FileInfo fileInfo, int burstCount = 1, CancellationToken cancellationToken = default)
    {
        if (!fileInfo.Exists)
        {
            _logger.LogError("[x] File {FilePath} not found", fileInfo.FullName);
            throw new FileNotFoundException($"File {fileInfo.FullName} not found", fileInfo.FullName);
        }

        if (fileInfo.Length > 0)
        {
            using var stream = fileInfo.OpenRead();
            using var reader = new StreamReader(stream);
            var message = reader.ReadToEnd();
            return PublishMessage(dest, message, burstCount, cancellationToken);
        }

        _logger.LogError("[x] File {FilePath} is empty", fileInfo.FullName);
        throw new ArgumentException($"File {fileInfo.FullName} is empty");
    }

    private async Task PublishViaTempKey(IChannel channel, string queue, string message, string? exchange, int burstCount = 1, CancellationToken cancellationToken = default)
    {
        // Use a temporary routing key for the message
        // TODO: Consider using a more robust temporary key generation strategy or a unique identifier
        var routingKey = $"temp-key-{queue}";

        // TODO: Handle case where default exchange does not exist
        // TODO: Consider case when exchange was set but does not exist
        var destExchange = exchange ?? "amq.direct";
        _logger.LogInformation("[*] Publishing {Count} message(s) to queue: {Queue}, routing-key: {RoutingKey}, exchange: {Exchange}...",
            burstCount, queue, routingKey, destExchange);

        _logger.LogDebug("[pub] Create temporary binding...");
        await channel.QueueBindAsync(queue, destExchange, routingKey, cancellationToken: cancellationToken);
        
        await Publish(channel, message, destExchange, routingKey, burstCount, cancellationToken);

        _logger.LogDebug("[pub] Remove temporary binding...");
        await channel.QueueUnbindAsync(queue, destExchange, routingKey, cancellationToken: cancellationToken);
    }
    
    private static async Task Publish(IChannel channel, string message, string exchange, string routingKey, int burstCount = 1,
        CancellationToken cancellationToken = default)
    {
#if !DEBUG
        var body = Encoding.UTF8.GetBytes(message);
#endif
        for (var i = 0; i < burstCount; i++)
        {
#if DEBUG
            var body = Encoding.UTF8.GetBytes($"#{i}: {message}");
#endif
            await channel.BasicPublishAsync(exchange, routingKey, true, body, cancellationToken);
        }
    }
}