using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using rmqctl.Configuration;
using rmqctl.Models;
using rmqctl.Utilities;

namespace rmqctl.Services;

public interface IConsumeService
{
    Task ConsumeMessages(string queue, AckModes ackMode, int messageCount = -1);
    Task DumpMessagesToFile(string queue, AckModes ackMode, FileInfo outputFileInfo, int messageCount = -1);
    Task StartContinuousConsumptionAsync(string queue, AckModes ackMode, int messageCount = -1, CancellationToken cancellationToken = default);
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

    public async Task ConsumeMessages(string queue, AckModes ackMode, int messageCount = -1)
    {
        _logger.LogInformation("Consume {Count} message(s) from '{Queue}' queue in '{AckMode}' mode",
            messageCount == -1 ? "all" : messageCount.ToString(), queue, ackMode);

        await using var channel = await _rabbitChannelFactory.GetChannelAsync();

        await foreach (var message in FetchMessagesAsync(channel, queue, ackMode, messageCount))
        {
            _logger.LogInformation("DeliveryTag: {DeliveryTag}\nHeaders:{Properties}Message:\n{Message}", message.deliveryTag,
                MessageFormater.FormatHeaders(message.props?.Headers), message.body);
        }
    }

    public async Task DumpMessagesToFile(string queue, AckModes ackMode, FileInfo outputFileInfo, int messageCount = -1)
    {
        _logger.LogInformation("Dump {Count} message(s) from '{Queue}' queue in '{AckMode}' mode to '{OutputFile}'",
            messageCount == -1 ? "all" : messageCount.ToString(), queue, ackMode, outputFileInfo.FullName);

        await using var channel = await _rabbitChannelFactory.GetChannelAsync();

        if (_fileConfig.MessagesPerFile >= messageCount)
        {
            await WriteMessagesToSingleFile(channel, queue, ackMode, outputFileInfo, messageCount);
        }
        else
        {
            await WriteMessagesToMultipleFiles(channel, queue, ackMode, outputFileInfo, messageCount);
        }
    }

    public async Task StartContinuousConsumptionAsync(string queue, AckModes ackMode, int messageCount = -1, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[*] Starting continuous consumption of messages from '{Queue}' queue in '{AckMode}' {StoppingCondition}",
            queue, ackMode, messageCount == -1 ? "until stopped" : $"until {messageCount.ToString()} messages are consumed");
        _logger.LogInformation("[*] Press Ctrl+C to stop the consumption...");

        // Create a local cancellation token source to allow stopping the consumption
        var localCts = new CancellationTokenSource();
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, localCts.Token);

        await using var channel = await _rabbitChannelFactory.GetChannelAsync();

        await channel.BasicQosAsync(0, _rabbitChannelFactory.PrefetchCount, false, linkedCts.Token);

        var receiveChan = Channel.CreateBounded<RabbitMessage>(_rabbitChannelFactory.PrefetchCount * 2);
        var ackChan = Channel.CreateUnbounded<(ulong deliveryTag, AckModes ackMode)>();

        // Hook up callback that completes the receive channel when message count is reached or cancellation is requested by user/applicaiton
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
            if (linkedCts.Token.IsCancellationRequested)
            {
                return;
            }

            _logger.LogInformation("[m-receiver] Received message #{DeliveryTag}", ea.DeliveryTag);
            var message = new RabbitMessage(
                System.Text.Encoding.UTF8.GetString(ea.Body.ToArray()),
                ea.DeliveryTag,
                ea.BasicProperties
            );

            // Simulate some processing delay (e.g., for testing purposes)
            // await Task.Delay(100);

            await receiveChan.Writer.WriteAsync(message, linkedCts.Token);
            _logger.LogDebug("[m-receiver] Message #{DeliveryTag} written to receive channel", ea.DeliveryTag);

            // Check if we reached the message count limit
            if (messageCount != -1 && Interlocked.Increment(ref receivedCount) == messageCount)
            {
                _logger.LogDebug("[m-receiver] Message limit reached ({MessageCount}) - initiating cancellation!", messageCount);
                await localCts.CancelAsync();
            }
        };

        _logger.LogDebug("[*] Starting RabbitMQ consumer for queue '{Queue}'", queue);
        _ = channel.BasicConsumeAsync(queue: queue, autoAck: false, consumer: consumer);

        // Process received messages
        var messageProcessor = Task.Run(async () =>
        {
            _logger.LogDebug("[m-proc] Starting message processing...");
            await foreach (var message in receiveChan.Reader.ReadAllAsync())
            {
                try
                {
                    _logger.LogInformation("[m-proc] Start processing message #{DeliveryTag}...", message.DeliveryTag);

                    // Simulate some processing delay (e.g., for testing purposes)
                    // await Task.Delay(100);

                    await ackChan.Writer.WriteAsync((message.DeliveryTag, ackMode));
                    _logger.LogDebug("[m-proc] Message #{DeliveryTag} processed successfully", message.DeliveryTag);
                }
                catch (Exception)
                {
                    _logger.LogWarning("[m-proc] Message #{DeliveryTag} failed to process", message.DeliveryTag);
                    await ackChan.Writer.WriteAsync((message.DeliveryTag, AckModes.Requeue));
                }
            }

            ackChan.Writer.TryComplete();
            _logger.LogDebug("[m-proc] Done!");
        });

        // Dispatcher for acknowledgments of successfully processed messages
        // TODO: handle multiple acks in a single call
        var ackDispatcher = Task.Run(async () =>
        {
            _logger.LogDebug("[ack-disp] Starting acknowledgment dispatcher...");
            await foreach (var (deliveryTag, ackModeValue) in ackChan.Reader.ReadAllAsync())
            {
                switch (ackModeValue)
                {
                    case AckModes.Ack:
                        _logger.LogDebug("[ack-disp] Acknowledging message #{DeliveryTag}", deliveryTag);
                        await channel.BasicAckAsync(deliveryTag, multiple: false);
                        break;
                    case AckModes.Reject:
                        _logger.LogDebug("[ack-disp] Rejecting message #{DeliveryTag} without requeue", deliveryTag);
                        await channel.BasicNackAsync(deliveryTag, multiple: false, requeue: false);
                        break;
                    case AckModes.Requeue:
                        _logger.LogDebug("[ack-disp] Requeuing message #{DeliveryTag}", deliveryTag);
                        await channel.BasicNackAsync(deliveryTag, multiple: false, requeue: true);
                        break;
                }
            }

            _logger.LogDebug("[ack-disp] Done!");
        });

        await Task.WhenAll(messageProcessor, ackDispatcher);

        _logger.LogDebug("[x] Continuous consumption stopped. Waiting for RabbitMQ channel to close...");
        await channel.CloseAsync();
    }

    private async Task WriteMessagesToSingleFile(IChannel channel, string queue, AckModes ackMode, FileInfo outputFileInfo, int messageCount)
    {
        await using var fileStream = outputFileInfo.OpenWrite();
        await using var writer = new StreamWriter(fileStream);

        await foreach (var message in FetchMessagesAsync(channel, queue, ackMode, messageCount))
        {
            await writer.WriteLineAsync(
                $"DeliveryTag: {message.deliveryTag}\nHeaders:{MessageFormater.FormatHeaders(message.props?.Headers)}Message:\n{message.body}");
            await writer.WriteLineAsync(_fileConfig.MessageDelimiter);
        }
    }

    private async Task WriteMessagesToMultipleFiles(IChannel channel, string queue, AckModes ackMode, FileInfo outputFileInfo, int messageCount)
    {
        var fileIndex = 0;
        var messagesInCurrentFile = 0;
        var baseFileName = Path.Combine(
            outputFileInfo.DirectoryName ?? string.Empty,
            Path.GetFileNameWithoutExtension(outputFileInfo.Name));
        var fileExtension = outputFileInfo.Extension;

        FileStream? fileStream = null;
        StreamWriter? writer = null;

        try
        {
            await foreach (var message in FetchMessagesAsync(channel, queue, ackMode, messageCount))
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

                await writer.WriteLineAsync(
                    $"DeliveryTag: {message.deliveryTag}\nHeaders:{MessageFormater.FormatHeaders(message.props?.Headers)}Message:\n{message.body}");
                await writer.WriteLineAsync(_fileConfig.MessageDelimiter);
                messagesInCurrentFile++;
            }
        }
        finally
        {
            if (writer != null)
            {
                await writer.DisposeAsync();
                await fileStream!.DisposeAsync();
            }
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

    private static async IAsyncEnumerable<(string body, ulong deliveryTag, IReadOnlyBasicProperties? props)> FetchMessagesAsync(
        IChannel channel,
        string queue,
        AckModes ackMode,
        int messageCount = -1
    )
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

                yield return (body, result.DeliveryTag, result.BasicProperties);

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

public class RabbitMessage
{
    public string Body { get; set; }
    public ulong DeliveryTag { get; set; }
    public IReadOnlyBasicProperties? Props { get; set; }

    public RabbitMessage(string body, ulong deliveryTag, IReadOnlyBasicProperties? props)
    {
        this.Body = body;
        this.DeliveryTag = deliveryTag;
        this.Props = props;
    }

    public override string ToString()
    {
        return "DeliveryTag: " + DeliveryTag + "\n" +
               "Headers: " + MessageFormater.FormatHeaders(Props?.Headers) + "\n" +
               "Message:\n" + Body;
    }
}