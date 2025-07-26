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
        _logger.LogDebug(
            "Initiating publish operation: exchange={Exchange}, routing-key={RoutingKey}, queue={Queue}, messageLength={Length}, burstCount={Count}",
            dest.Exchange, dest.RoutingKey, dest.Queue, message.Length, burstCount);
        try
        {
            await using var channel = await _rabbitChannelFactory.GetChannelAsync();

            await Publish(channel, message, dest.Exchange ?? string.Empty,
                dest.Queue ?? dest.RoutingKey ?? throw new ArgumentException("Either a queue or a combination of exchange and routing key must be specified"),
                burstCount, cancellationToken);
        }
        catch (OperationInterruptedException e)
        {
            _logger.LogError(e, "[x] Failed to publish message with target {Destination}: {Message}",
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

    private static async Task Publish(
        IChannel channel,
        string message,
        string exchange,
        string routingKey,
        int burstCount = 1,
        CancellationToken cancellationToken = default)
    {
        var body = Encoding.UTF8.GetBytes(message);
        for (var i = 0; i < burstCount; i++)
        {
            await channel.BasicPublishAsync(exchange, routingKey, true, body, cancellationToken);
        }
    }
}