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
        _logger.LogInformation("Starting continuous consumption of messages from '{Queue}' queue in '{AckMode}' {StoppingCondition}",
            queue, ackMode, messageCount == -1 ? "until stopped" : $"until {messageCount.ToString()} messages are consumed");
        _logger.LogInformation("Press Ctrl+C to stop the consumption...");

        using var countBasedCts = new CancellationTokenSource();
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, countBasedCts.Token);
        var combinedToken = linkedCts.Token;

        await using var channel = await _rabbitChannelFactory.GetChannelAsync();

        var consumer = new AsyncEventingBasicConsumer(channel);

        var processedCount = 0;

        // Register message received callback
        consumer.ReceivedAsync += async (sender, @event) =>
        {
            if (combinedToken.IsCancellationRequested)
            {
                _logger.LogDebug("Cancellation requested, not handling current event...");
                await channel.BasicNackAsync(@event.DeliveryTag, false, true, combinedToken);
                return;
            }
            if (processedCount >= messageCount)
            {
                await channel.BasicNackAsync(@event.DeliveryTag, false, true, combinedToken);

                countBasedCts.Cancel();
                return;
            }

            var body = System.Text.Encoding.UTF8.GetString(@event.Body.ToArray());
            _logger.LogInformation("{DeliveryTag}: {Body}", @event.DeliveryTag, body);
            switch (ackMode)
            {
                case AckModes.Ack:
                    await channel.BasicAckAsync(@event.DeliveryTag, false, combinedToken);
                    break;
                case AckModes.Reject:
                    await channel.BasicNackAsync(@event.DeliveryTag, false, false, combinedToken);
                    break;
                case AckModes.Requeue:
                    await channel.BasicNackAsync(@event.DeliveryTag, false, true, combinedToken);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(ackMode), ackMode, null);
            }

            await Task.Delay(1000, combinedToken);
            processedCount++;
            if (messageCount == processedCount)
            {
                _logger.LogDebug("Message count reached, stopping consumption...");
                countBasedCts.Cancel();
            }
        };

        // Register consumer shutdown callback
        consumer.ShutdownAsync += (sender, @event) =>
        {
            _logger.LogWarning("Consumer shutdown: {Reason}", @event.ReplyText);
            return Task.CompletedTask;
        };

        var consumerTag = await channel.BasicConsumeAsync(queue, false, consumer, combinedToken);

        try
        {
            await Task.Delay(Timeout.Infinite, combinedToken);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Cancellation requested, stopping consumer...");
            await channel.BasicCancelAsync(consumerTag);
        }
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