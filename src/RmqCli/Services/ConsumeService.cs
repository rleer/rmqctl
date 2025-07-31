using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RmqCli.MessageWriter;
using RmqCli.Models;

namespace RmqCli.Services;

public interface IConsumeService
{
    Task ConsumeMessages(string queue, AckModes ackMode, FileInfo? outputFileInfo = null, int messageCount = -1, OutputFormat outputFormat = OutputFormat.Plain, CancellationToken cancellationToken = default);
}

public class ConsumeService : IConsumeService
{
    private readonly ILogger<ConsumeService> _logger;
    private readonly IRabbitChannelFactory _rabbitChannelFactory;
    private readonly IMessageWriterFactory _messageWriterFactory;

    public ConsumeService(ILogger<ConsumeService> logger, IRabbitChannelFactory rabbitChannelFactory, IMessageWriterFactory messageWriterFactory)
    {
        _logger = logger;
        _rabbitChannelFactory = rabbitChannelFactory;
        _messageWriterFactory = messageWriterFactory;
    }

    public async Task ConsumeMessages(
        string queue,
        AckModes ackMode,
        FileInfo? outputFileInfo,
        int messageCount = -1,
        OutputFormat outputFormat = OutputFormat.Plain,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[*] Starting consuming from '{Queue}' queue in '{AckMode}' {StoppingCondition}",
            queue, ackMode, messageCount == -1 ? "until stopped" : $"until {messageCount.ToString()} messages are consumed");

        // Create a local cancellation token source to allow stopping the consumption
        var localCts = new CancellationTokenSource();
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, localCts.Token);

        await using var channel = await _rabbitChannelFactory.GetChannelAsync();

        var receiveChan = Channel.CreateUnbounded<RabbitMessage>();
        var ackChan = Channel.CreateUnbounded<(ulong deliveryTag, AckModes ackMode)>();

        // Hook up callback that completes the receive-channel when message count is reached or cancellation is requested by user/applicaiton
        linkedCts.Token.Register(() =>
        {
            _logger.LogDebug("[x] Completing receive channel (host: {ParentCt}, local: {LocalCt})",
                cancellationToken.IsCancellationRequested, localCts.IsCancellationRequested);
            receiveChan.Writer.TryComplete();
        });

        long receivedCount = 0;

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += async (_, ea) =>
        {
            // Skip messages if cancellation is requested. Skipped and unack'd messages will be automatically requeued by RabbitMQ once the channel closes.
            if (linkedCts.Token.IsCancellationRequested)
            {
                return;
            }

            _logger.LogDebug("[*] Received message #{DeliveryTag}", ea.DeliveryTag);
            var message = new RabbitMessage(
                System.Text.Encoding.UTF8.GetString(ea.Body.ToArray()),
                ea.DeliveryTag,
                ea.BasicProperties,
                ea.Redelivered
            );

            await receiveChan.Writer.WriteAsync(message, linkedCts.Token);
            _logger.LogDebug("[*] Message #{DeliveryTag} written to receive channel", ea.DeliveryTag);

            // Check if we reached the message count limit
            if (messageCount != -1 && Interlocked.Increment(ref receivedCount) == messageCount)
            {
                _logger.LogDebug("[*] Message limit reached ({MessageCount}) - initiating cancellation!", messageCount);
                await localCts.CancelAsync();
            }
        };

        _logger.LogDebug("[*] Starting RabbitMQ consumer for queue '{Queue}'", queue);

        // Start consuming messages from the specified queue
        _ = channel.BasicConsumeAsync(queue: queue, autoAck: false, consumer: consumer);

        // Start processing received messages
        var messageWriter = _messageWriterFactory.CreateWriter(outputFileInfo, messageCount, outputFormat);
        var writerTask = Task.Run(() =>
            messageWriter.WriteMessageAsync(receiveChan, ackChan, ackMode));

        // Start dispatcher for acknowledgments of successfully processed messages
        var ackDispatcher = Task.Run(() => HandleAcks(ackChan, channel));

        await Task.WhenAll(writerTask, ackDispatcher);

        _logger.LogDebug("[x] Continuous consumption stopped. Waiting for RabbitMQ channel to close...");
        await channel.CloseAsync();
    }

    private async Task HandleAcks(Channel<(ulong deliveryTag, AckModes ackMode)> ackChan, IChannel rmqChannel)
    {
        // TODO: handle multiple acks in a single call
        _logger.LogDebug("[*] Starting acknowledgment dispatcher...");
        await foreach (var (deliveryTag, ackModeValue) in ackChan.Reader.ReadAllAsync())
        {
            switch (ackModeValue)
            {
                case AckModes.Ack:
                    _logger.LogDebug("[*] Acknowledging message #{DeliveryTag}", deliveryTag);
                    await rmqChannel.BasicAckAsync(deliveryTag, multiple: false);
                    break;
                case AckModes.Reject:
                    _logger.LogDebug("[*] Rejecting message #{DeliveryTag} without requeue", deliveryTag);
                    await rmqChannel.BasicNackAsync(deliveryTag, multiple: false, requeue: false);
                    break;
                case AckModes.Requeue:
                    _logger.LogDebug("[*] Requeue message #{DeliveryTag}", deliveryTag);
                    await rmqChannel.BasicNackAsync(deliveryTag, multiple: false, requeue: true);
                    break;
            }
        }

        _logger.LogDebug("[x] Acknowledgement dispatcher done!");
    }
}