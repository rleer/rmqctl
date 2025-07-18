using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using rmqctl.Configuration;
using rmqctl.Models;

namespace rmqctl.MessageWriter;

public class RotatingFileMessageWriter : IMessageWriter
{
    private readonly ILogger<RotatingFileMessageWriter> _logger;
    private readonly FileConfig _fileConfig;
    private FileInfo? _outputFileInfo;

    public RotatingFileMessageWriter(ILogger<RotatingFileMessageWriter> logger, IOptions<FileConfig> fileConfig)
    {
        _logger = logger;
        _fileConfig = fileConfig.Value;
    }

    public IMessageWriter Initialize(FileInfo? outputFileInfo)
    {
        _outputFileInfo = outputFileInfo;
        return this;
    }

    public async Task WriteMessageAsync(
        Channel<RabbitMessage> messageChannel,
        Channel<(ulong deliveryTag, AckModes ackMode)> ackChannel,
        AckModes ackMode)
    {
        _logger.LogDebug("[*] Starting message processing...");

        if (_outputFileInfo == null)
        {
            _logger.LogError("[x] Output file info is null. Cannot write messages.");
            throw new InvalidOperationException("Output file info must be set before writing messages.");
        }

        // TODO: Refactor to just use StreamWriter without explicit FileStream management
        FileStream? fileStream = null;
        StreamWriter? writer = null;
        try
        {
            var fileIndex = 0;
            var messagesInCurrentFile = 0;
            var baseFileName = Path.Combine(
                _outputFileInfo.DirectoryName ?? string.Empty,
                Path.GetFileNameWithoutExtension(_outputFileInfo.Name));
            var fileExtension = _outputFileInfo.Extension;

            await foreach (var message in messageChannel.Reader.ReadAllAsync())
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

                _logger.LogDebug("[*] Start processing message #{DeliveryTag}...", message.DeliveryTag);

                await writer.WriteLineAsync(message.ToString());
                await writer.WriteLineAsync(_fileConfig.MessageDelimiter);
                messagesInCurrentFile++;

                await ackChannel.Writer.WriteAsync((message.DeliveryTag, ackMode));
                _logger.LogDebug("[*] Message #{DeliveryTag} processed successfully", message.DeliveryTag);
            }
        }
        // TODO: handle specific exceptions like IOException, UnauthorizedAccessException, etc.
        catch (Exception)
        {
            _logger.LogError("[x] Failed to write messages to file '{FileName}'", fileStream?.Name);
            throw;
        }
        finally
        {
            ackChannel.Writer.TryComplete();

            if (writer != null)
            {
                await writer.DisposeAsync();
                await fileStream!.DisposeAsync();
            }

            _logger.LogDebug("[*] Done!");
        }
    }

    private (FileStream fileStream, StreamWriter writer) CreateNewFile(string baseFileName, string fileExtension, int fileIndex)
    {
        var currentFileName = $"{baseFileName}.{fileIndex}{fileExtension}";
        _logger.LogDebug("[*] Creating new file: {FileName}", currentFileName);

        var fileStream = new FileStream(currentFileName, FileMode.Create, FileAccess.Write);
        var writer = new StreamWriter(fileStream);

        return (fileStream, writer);
    }
}