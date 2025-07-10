using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using rmqctl.Configuration;
using rmqctl.Models;

namespace rmqctl.Services;

public interface IConsumeService
{
    Task ConsumeMessages(string queue, AckModes ackMode, FileInfo? outputFileInfo = null, int messageCount = -1, CancellationToken cancellationToken = default);
}

public class ConsumeService : IConsumeService
{
    private readonly ILogger<ConsumeService> _logger;
    private readonly IRabbitChannelFactory _rabbitChannelFactory;
    private readonly FileConfig _fileConfig;

    public ConsumeService(ILogger<ConsumeService> logger, IRabbitChannelFactory rabbitChannelFactory, IOptions<FileConfig> fileConfig)
    {
        _logger = logger;
        _rabbitChannelFactory = rabbitChannelFactory;
        _fileConfig = fileConfig.Value;
    }

    public async Task ConsumeMessages(string queue, AckModes ackMode, FileInfo? outputFileInfo, int messageCount = -1,
        CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[*] Starting consuming from '{Queue}' queue in '{AckMode}' {StoppingCondition}",
            queue, ackMode, messageCount == -1 ? "until stopped" : $"until {messageCount.ToString()} messages are consumed");

        // Create a local cancellation token source to allow stopping the consumption
        var localCts = new CancellationTokenSource();
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, localCts.Token);

        await using var channel = await _rabbitChannelFactory.GetChannelAsync();

        // Caution: Prefetch count might cause redelivery loop
        // await channel.BasicQosAsync(0, _rabbitChannelFactory.PrefetchCount, false, linkedCts.Token);

        var receiveChan = Channel.CreateBounded<RabbitMessage>(_rabbitChannelFactory.PrefetchCount * 2);
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

            _logger.LogDebug("[m-recv] Received message #{DeliveryTag}", ea.DeliveryTag);
            var message = new RabbitMessage(
                System.Text.Encoding.UTF8.GetString(ea.Body.ToArray()),
                ea.DeliveryTag,
                ea.BasicProperties,
                ea.Redelivered
            );

            await receiveChan.Writer.WriteAsync(message, linkedCts.Token);
            _logger.LogDebug("[m-recv] Message #{DeliveryTag} written to receive channel", ea.DeliveryTag);

            // Check if we reached the message count limit
            if (messageCount != -1 && Interlocked.Increment(ref receivedCount) == messageCount)
            {
                _logger.LogDebug("[m-recv] Message limit reached ({MessageCount}) - initiating cancellation!", messageCount);
                await localCts.CancelAsync();
            }
        };

        _logger.LogDebug("[*] Starting RabbitMQ consumer for queue '{Queue}'", queue);

        // Start consuming messages from the specified queue
        _ = channel.BasicConsumeAsync(queue: queue, autoAck: false, consumer: consumer);

        // Start processing received messages
        Task messageProcessor;
        if (outputFileInfo is null)
        {
            messageProcessor = Task.Run(() => WriteMessagesToLog(receiveChan, ackChan, ackMode));
        }
        else if (messageCount != -1 && _fileConfig.MessagesPerFile >= messageCount)
        {
            messageProcessor = Task.Run(() => WriteMessagesToSingleFile(receiveChan, ackChan, ackMode, outputFileInfo));
        }
        else
        {
            messageProcessor = Task.Run(() => WriteMessagesToMultipleFiles(receiveChan, ackChan, ackMode, outputFileInfo));
        }

        // Start dispatcher for acknowledgments of successfully processed messages
        var ackDispatcher = Task.Run(async () => HandleAcks(ackChan, channel));

        await Task.WhenAll(messageProcessor, ackDispatcher);

        _logger.LogDebug("[x] Continuous consumption stopped. Waiting for RabbitMQ channel to close...");
        await channel.CloseAsync();
    }

    private async Task HandleAcks(Channel<(ulong deliveryTag, AckModes ackMode)> ackChan, IChannel rmqChannel)
    {
        // TODO: handle multiple acks in a single call
        _logger.LogDebug("[ack-disp] Starting acknowledgment dispatcher...");
        await foreach (var (deliveryTag, ackModeValue) in ackChan.Reader.ReadAllAsync())
        {
            switch (ackModeValue)
            {
                case AckModes.Ack:
                    _logger.LogDebug("[ack-disp] Acknowledging message #{DeliveryTag}", deliveryTag);
                    await rmqChannel.BasicAckAsync(deliveryTag, multiple: false);
                    break;
                case AckModes.Reject:
                    _logger.LogDebug("[ack-disp] Rejecting message #{DeliveryTag} without requeue", deliveryTag);
                    await rmqChannel.BasicNackAsync(deliveryTag, multiple: false, requeue: false);
                    break;
                case AckModes.Requeue:
                    _logger.LogDebug("[ack-disp] Requeue message #{DeliveryTag}", deliveryTag);
                    await rmqChannel.BasicNackAsync(deliveryTag, multiple: false, requeue: true);
                    break;
            }
        }

        _logger.LogDebug("[ack-disp] Done!");
    }

    private async Task WriteMessagesToLog(
        Channel<RabbitMessage> messageCache,
        Channel<(ulong deliveryTag, AckModes ackMode)> ackCache,
        AckModes ackMode)
    {
        _logger.LogDebug("[m-proc] Starting message processing...");
        await foreach (var message in messageCache.Reader.ReadAllAsync())
        {
            try
            {
                _logger.LogInformation(message.ToString());
                await ackCache.Writer.WriteAsync((message.DeliveryTag, ackMode));
                _logger.LogDebug("[m-proc] Message #{DeliveryTag} processed successfully", message.DeliveryTag);
            }
            catch (Exception)
            {
                _logger.LogWarning("[m-proc] Message #{DeliveryTag} failed to process", message.DeliveryTag);
                await ackCache.Writer.WriteAsync((message.DeliveryTag, AckModes.Requeue));
            }
        }

        ackCache.Writer.TryComplete();
        _logger.LogDebug("[m-proc] Done!");
    }

    private async Task WriteMessagesToSingleFile(
        Channel<RabbitMessage> messageCache,
        Channel<(ulong deliveryTag, AckModes ackMode)> ackCache,
        AckModes ackMode,
        FileInfo outputFileInfo)
    {
        _logger.LogDebug("[m-proc] Starting message processing...");

        try
        {
            await using var fileStream = outputFileInfo.OpenWrite();
            await using var writer = new StreamWriter(fileStream);

            await foreach (var message in messageCache.Reader.ReadAllAsync())
            {
                _logger.LogDebug("[m-proc] Start processing message #{DeliveryTag}...", message.DeliveryTag);
                await writer.WriteLineAsync(message.ToString());
                await writer.WriteLineAsync(_fileConfig.MessageDelimiter);

                await ackCache.Writer.WriteAsync((message.DeliveryTag, ackMode));
                _logger.LogDebug("[m-proc] Message #{DeliveryTag} processed successfully", message.DeliveryTag);
            }

            await writer.FlushAsync();
        }
        catch (Exception)
        {
            _logger.LogError("[m-proc] Failed to write messages to file '{FileName}'", outputFileInfo.FullName);
        }
        finally
        {
            ackCache.Writer.TryComplete();
            _logger.LogDebug("[m-proc] Done!");
        }
    }

    private async Task WriteMessagesToMultipleFiles(
        Channel<RabbitMessage> messageCache,
        Channel<(ulong deliveryTag, AckModes ackMode)> ackCache,
        AckModes ackMode,
        FileInfo outputFileInfo)
    {
        FileStream? fileStream = null;
        StreamWriter? writer = null;
        try
        {
            var fileIndex = 0;
            var messagesInCurrentFile = 0;
            var baseFileName = Path.Combine(
                outputFileInfo.DirectoryName ?? string.Empty,
                Path.GetFileNameWithoutExtension(outputFileInfo.Name));
            var fileExtension = outputFileInfo.Extension;

            await foreach (var message in messageCache.Reader.ReadAllAsync())
            {
                if (writer is null || messagesInCurrentFile >= _fileConfig.MessagesPerFile)
                {
                    // Dispose the previous writer and file stream if they exist
                    if (writer is not null)
                    {
                        await writer.FlushAsync();
                        await writer.DisposeAsync();
                        await fileStream!.DisposeAsync();
                    }

                    (fileStream, writer) = CreateNewFile(baseFileName, fileExtension, fileIndex++);
                    messagesInCurrentFile = 0;
                }

                _logger.LogDebug("[m-proc] Start processing message #{DeliveryTag}...", message.DeliveryTag);

                await writer.WriteLineAsync(message.ToString());
                await writer.WriteLineAsync(_fileConfig.MessageDelimiter);
                messagesInCurrentFile++;

                await ackCache.Writer.WriteAsync((message.DeliveryTag, ackMode));
                _logger.LogDebug("[m-proc] Message #{DeliveryTag} processed successfully", message.DeliveryTag);
            }
        }
        // TODO: handle specific exceptions like IOException, UnauthorizedAccessException, etc.
        catch (Exception)
        {
            _logger.LogError("[m-proc] Failed to write messages to file '{FileName}'", fileStream?.Name);
            throw;
        }
        finally
        {
            ackCache.Writer.TryComplete();

            if (writer != null)
            {
                await writer.DisposeAsync();
                await fileStream!.DisposeAsync();
            }

            _logger.LogDebug("[m-proc] Done!");
        }
    }

    private (FileStream fileStream, StreamWriter writer) CreateNewFile(string baseFileName, string fileExtension, int fileIndex)
    {
        var currentFileName = $"{baseFileName}.{fileIndex}{fileExtension}";
        _logger.LogDebug("Creating new file: {FileName}", currentFileName);

        var fileStream = new FileStream(currentFileName, FileMode.Create, FileAccess.Write);
        var writer = new StreamWriter(fileStream);

        return (fileStream, writer);
    }
}