using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using rmqctl.Configuration;
using rmqctl.Models;

namespace rmqctl.Services;

public interface IConsumeService
{
    Task ConsumeMessages(string queue, AckModes ackMode, int messageCount = -1);
    Task DumpMessagesToFile(string queue, AckModes ackMode, FileInfo outputFileInfo, int messageCount = -1);
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
            _logger.LogInformation("{DeliveryTag}: {Message}", message.deliveryTag, message.body);
        }
    }

    public async Task DumpMessagesToFile(string queue, AckModes ackMode, FileInfo outputFileInfo, int messageCount = -1)
    {
        _logger.LogInformation("Dump {Count} message(s) from '{Queue}' queue in '{AckMode}' mode to '{OutputFile}'",
            messageCount == -1 ? "all" : messageCount.ToString(), queue, ackMode, outputFileInfo.FullName);
        
        await using var channel = await _rabbitChannelFactory.GetChannelAsync();
        
        if (_fileConfig.MessagesPerFile <= messageCount)
        {
            // If the number of messages to be dumped is less than or equal to the configured limit,
            // write all messages to a single file without splitting.
            await using var fileStream = outputFileInfo.OpenWrite();
            await using var writer = new StreamWriter(fileStream);
            await foreach (var message in FetchMessagesAsync(channel, queue, ackMode, messageCount))
            {
                await writer.WriteLineAsync($"{message.deliveryTag}: {message.body}");
                await writer.WriteLineAsync(_fileConfig.MessageDelimiter);
            } 
        }
        else
        {
            // If the number of messages to be dumped exceeds the configured limit,
            // split the messages into multiple files.
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
                    // Create new file if it's the first message, or if the current file is full
                    if (writer is null || messagesInCurrentFile >= _fileConfig.MessagesPerFile)
                    {
                        if (writer is not null)
                        {
                            await writer.DisposeAsync();
                            await fileStream!.DisposeAsync();
                        }
                        
                        // baseFileName.0.txt, baseFileName.1.txt, etc.
                        var currentFileName = $"{baseFileName}.{fileIndex}{fileExtension}";
                        
                        _logger.LogDebug("Creating new file: {FileName}", currentFileName);
                        fileStream = new FileStream(currentFileName, FileMode.Create, FileAccess.Write);
                        writer = new StreamWriter(fileStream);
                        fileIndex++;
                        messagesInCurrentFile = 0;
                    }
                    
                    // Write the message to the current file
                    await writer.WriteLineAsync($"{message.deliveryTag}: {message.body}");
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