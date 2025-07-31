using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RmqCli.Configuration;
using RmqCli.MessageFormatter;
using RmqCli.Models;

namespace RmqCli.MessageWriter;

public class RotatingFileMessageWriter : IMessageWriter
{
    private readonly ILogger<RotatingFileMessageWriter> _logger;
    private readonly FileConfig _fileConfig;
    private readonly IMessageFormatterFactory _formatterFactory;
    private FileInfo? _outputFileInfo;
    private OutputFormat _outputFormat = OutputFormat.Plain;
    private IMessageFormatter? _formatter;

    public RotatingFileMessageWriter(ILogger<RotatingFileMessageWriter> logger, FileConfig fileConfig, IMessageFormatterFactory formatterFactory)
    {
        _logger = logger;
        _fileConfig = fileConfig;
        _formatterFactory = formatterFactory;
    }

    public IMessageWriter Initialize(FileInfo? outputFileInfo, OutputFormat outputFormat = OutputFormat.Plain)
    {
        _outputFileInfo = outputFileInfo;
        _outputFormat = outputFormat;
        _formatter = _formatterFactory.CreateFormatter(outputFormat);
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

        if (_formatter == null)
        {
            _logger.LogError("[x] Message formatter not set. Cannot write messages.");
            throw new InvalidOperationException("Message formatter not set. Cannot write messages.");
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
                    
                    if (_outputFormat is OutputFormat.Json)
                    {
                        await writer.WriteLineAsync("[");
                    }
                }

                _logger.LogDebug("[*] Start processing message #{DeliveryTag}...", message.DeliveryTag);

                if (messagesInCurrentFile != 0)
                {
                    if (_outputFormat is OutputFormat.Json)
                    {
                        await writer.WriteLineAsync(","); // Add comma for JSON formatting
                    }
                    else if (_outputFormat is OutputFormat.Plain)
                    {
                        await writer.WriteLineAsync(_fileConfig.MessageDelimiter); // Add delimiter for text format
                    }
                }
                
                var formattedMessage = _formatter.FormatMessage(message);
                await writer.WriteLineAsync(formattedMessage);
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