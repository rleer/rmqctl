using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using rmqctl.Models;

namespace rmqctl.Services;

public interface IPublishService
{
    Task PublishMessage(Destination dest, string message);
    Task PublishMessageFromFile(Destination dest, FileInfo fileInfo);
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

    public async Task PublishMessage(Destination dest, string message)
    {
        var body = System.Text.Encoding.UTF8.GetBytes(message);

        try
        {
            if (dest.Queue is not null)
            {
                await PublishViaTempKey(dest.Queue, body, dest.Exchange);
            }
            else if (dest.Exchange is not null && dest.RoutingKey is not null)
            {
                await PublishViaExchange(dest.RoutingKey, body, dest.Exchange);
            }
            else
            {
                throw new ArgumentException("Either a queue or an exchange and routing key must be specified");
            }
        }
        catch (OperationInterruptedException e)
        {
            _logger.LogError("Failed to publish message with target {Destination}: {Message}",
                dest.Queue ?? $"{dest.RoutingKey} via {dest.Exchange}",
                e.Message);
        }
    }

    public Task PublishMessageFromFile(Destination dest, FileInfo fileInfo)
    {
        if (!fileInfo.Exists)
        {
            _logger.LogError("File {FilePath} not found", fileInfo.FullName);
            throw new FileNotFoundException($"File {fileInfo.FullName} not found", fileInfo.FullName);
        }

        if (fileInfo.Length > 0)
        {
            using var stream = fileInfo.OpenRead();
            using var reader = new StreamReader(stream);
            var message = reader.ReadToEnd();
            return PublishMessage(dest, message);
        }

        _logger.LogError("File {FilePath} is empty", fileInfo.FullName);
        throw new ArgumentException($"File {fileInfo.FullName} is empty");
    }

    private async Task PublishViaExchange(string routingKey, byte[] body, string exchange)
    {
        await using var channel = await _rabbitChannelFactory.GetChannelAsync();
        _logger.LogInformation("Publishing message to {Exchange} with routing key {RoutingKey}", exchange, routingKey);

        await channel.BasicPublishAsync(exchange, routingKey, true, body);
    }

    private async Task PublishViaTempKey(string queue, byte[] body, string? exchange)
    {
        await using var channel = await _rabbitChannelFactory.GetChannelAsync();

        // Use a temporary routing key for the message
        var routingKey = $"temp-key-{queue}";

        var destExchange = exchange ?? "amq.direct";
        _logger.LogInformation("Publishing message to queue {Queue} via temporary routing key {RoutingKey} and exchange {Exchange}",
            queue, routingKey, destExchange);

        await channel.QueueBindAsync(queue, destExchange, routingKey);

        await channel.BasicPublishAsync(destExchange, routingKey, body);

        // Remove temporary binding after publishing
        await channel.QueueUnbindAsync(queue, destExchange, routingKey);
    }
}