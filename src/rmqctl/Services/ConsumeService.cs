using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using rmqctl.Models;

namespace rmqctl.Services;

public interface IConsumeService
{
    Task ConsumeMessages(string queue, AckModes ackMode, int messageCount = -1);
}

public class ConsumeService : IConsumeService
{
    private readonly ILogger<ConsumeService> _logger;
    private readonly IRabbitChannelFactory _rabbitChannelFactory;

    public ConsumeService(ILogger<ConsumeService> logger, IRabbitChannelFactory rabbitChannelFactory)
    {
        _logger = logger;
        _rabbitChannelFactory = rabbitChannelFactory;
    }

    public async Task ConsumeMessages(string queue, AckModes ackMode, int messageCount = -1)
    {
        // Validate the input parameter
        if (messageCount == 0)
        {
            _logger.LogWarning("Message count is 0. No messages will be consumed.");
            return;
        }
        _logger.LogInformation("Consume {Count} message(s) from '{Queue}' queue in '{AckMode}' mode",
            messageCount == -1 ? "all" : messageCount.ToString(), queue, ackMode);

        await using var channel = await _rabbitChannelFactory.GetChannelAsync();

        await foreach (var message in FetchMessagesAsync(channel, queue, ackMode, messageCount))
        {
            _logger.LogInformation("{DeliveryTag}: {Message}", message.deliveryTag, message.body);
        }
    }

    private static async IAsyncEnumerable<(string body, ulong deliveryTag)> FetchMessagesAsync(IChannel channel, string queue, AckModes ackMode, int messageCount = -1)
    {
        var processedCount = 0;
        var lastDeliveryTag = 0UL;

        while (messageCount == -1 || processedCount < messageCount)
        {
            var result = ackMode switch
            {
                AckModes.Ack => await channel.BasicGetAsync(queue, autoAck: true),
                AckModes.Reject or AckModes.Requeue => await channel.BasicGetAsync(queue, autoAck: false),
                _ => null
            };

            if (result != null)
            {
                lastDeliveryTag = result.DeliveryTag;
                var body = System.Text.Encoding.UTF8.GetString(result.Body.ToArray());

                yield return (body, result.DeliveryTag);

                processedCount++;
            }
            else
            {
                // No more messages in the queue - reject or requeue all fetched messages
                switch (ackMode)
                {
                    case AckModes.Reject:
                        await channel.BasicNackAsync(lastDeliveryTag, true, false);
                        break;
                    case AckModes.Requeue:
                        await channel.BasicNackAsync(lastDeliveryTag, true, true);
                        break;
                    case AckModes.Ack:
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(ackMode), ackMode, null);
                }

                yield break;
            }
        }
    }
}