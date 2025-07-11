using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using rmqctl.Models;

namespace rmqctl.MessageWriter;

public class ConsoleMessageWriter : IMessageWriter
{
    private readonly ILogger<ConsoleMessageWriter> _logger;

    public ConsoleMessageWriter(ILogger<ConsoleMessageWriter> logger)
    {
        _logger = logger;
    }
    
    public IMessageWriter Initialize(FileInfo? outputFileInfo)
    {
        // No initialization needed for console writer
        return this;
    }

    public async Task WriteMessageAsync(Channel<RabbitMessage> messageChannel, Channel<(ulong deliveryTag, AckModes ackMode)> ackChannel, AckModes ackMode)
    {
        _logger.LogDebug("[*] Starting message processing...");
        await foreach (var message in messageChannel.Reader.ReadAllAsync())
        {
            try
            {
                _logger.LogInformation(message.ToString());
                await ackChannel.Writer.WriteAsync((message.DeliveryTag, ackMode));
                _logger.LogDebug("[*] Message #{DeliveryTag} processed successfully", message.DeliveryTag);
            }
            catch (Exception)
            {
                _logger.LogWarning("[*] Message #{DeliveryTag} failed to process", message.DeliveryTag);
                await ackChannel.Writer.WriteAsync((message.DeliveryTag, AckModes.Requeue));
            }
        }

        ackChannel.Writer.TryComplete();
        _logger.LogDebug("[*] Done!");
    }
}