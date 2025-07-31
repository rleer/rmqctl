using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using RmqCli.MessageFormatter;
using RmqCli.Models;

namespace RmqCli.MessageWriter;

public class ConsoleMessageWriter : IMessageWriter
{
    private readonly ILogger<ConsoleMessageWriter> _logger;
    private readonly IMessageFormatterFactory _formatterFactory;
    private IMessageFormatter? _formatter;

    public ConsoleMessageWriter(ILogger<ConsoleMessageWriter> logger, IMessageFormatterFactory formatterFactory)
    {
        _logger = logger;
        _formatterFactory = formatterFactory;
    }
    
    public IMessageWriter Initialize(FileInfo? outputFileInfo, OutputFormat outputFormat = OutputFormat.Plain)
    {
        _formatter = _formatterFactory.CreateFormatter(outputFormat);
        return this;
    }

    public async Task WriteMessageAsync(Channel<RabbitMessage> messageChannel, Channel<(ulong deliveryTag, AckModes ackMode)> ackChannel, AckModes ackMode)
    {
        _logger.LogDebug("[*] Starting message processing...");
        
        if (_formatter == null)
        {
            throw new InvalidOperationException("Message writer must be initialized before use.");
        }
        
        await foreach (var message in messageChannel.Reader.ReadAllAsync())
        {
            try
            {
                _logger.LogInformation("{Message}", _formatter.FormatMessage(message));
                await ackChannel.Writer.WriteAsync((message.DeliveryTag, ackMode));
                _logger.LogDebug("[*] Message #{DeliveryTag} processed successfully", message.DeliveryTag);
            }
            catch (Exception e)
            {
                _logger.LogWarning("[*] Message #{DeliveryTag} failed to process: {Message}", message.DeliveryTag, e.Message);
                await ackChannel.Writer.WriteAsync((message.DeliveryTag, AckModes.Requeue));
            }
        }

        ackChannel.Writer.TryComplete();
        _logger.LogDebug("[*] Done!");
    }
}