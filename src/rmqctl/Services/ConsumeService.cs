using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using rmqctl.Models;

namespace rmqctl.Services;

public interface IConsumeService
{
    Task ConsumeMessages(string queue, AckModes ackMode);
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

    public async Task ConsumeMessages(string queue, AckModes ackMode)
    {
        await using var channel = await _rabbitChannelFactory.GetChannelAsync();

        var result = ackMode switch
        {
            AckModes.Ack => await channel.BasicGetAsync(queue, autoAck: true),
            AckModes.Reject or AckModes.Requeue => await channel.BasicGetAsync(queue, autoAck: false),
            _ => null
        };

        if (result != null)
        {
            var body = System.Text.Encoding.UTF8.GetString(result.Body.ToArray());
            _logger.LogInformation("Received: {Message}", body);

            switch (ackMode)
            {
                case AckModes.Reject:
                    await channel.BasicRejectAsync(result.DeliveryTag, requeue: false); 
                    break;
                case AckModes.Requeue:
                    await channel.BasicRejectAsync(result.DeliveryTag, requeue: true);
                    break;
                case AckModes.Ack:
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(ackMode), ackMode, null);
            }
        }
    }
}