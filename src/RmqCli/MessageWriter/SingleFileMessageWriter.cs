using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RmqCli.Configuration;
using RmqCli.MessageFormatter;
using RmqCli.Models;

namespace RmqCli.MessageWriter;

public class SingleFileMessageWriter : IMessageWriter
{
    private readonly ILogger<SingleFileMessageWriter> _logger;
    private readonly FileConfig _fileConfig;
    private readonly IMessageFormatterFactory _formatterFactory;
    private FileInfo? _outputFileInfo;
    private OutputFormat _outputFormat = OutputFormat.Plain;
    private IMessageFormatter? _formatter;

    public SingleFileMessageWriter(ILogger<SingleFileMessageWriter> logger, FileConfig fileConfig, IMessageFormatterFactory formatterFactory)
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

    public async Task WriteMessageAsync(Channel<RabbitMessage> messageChannel, Channel<(ulong deliveryTag, AckModes ackMode)> ackChannel, AckModes ackMode)
    {
        _logger.LogDebug("[*] Starting message processing...");

        if (_outputFileInfo == null)
        {
            _logger.LogError("[x] Output file info is null. Cannot write messages.");
            throw new InvalidOperationException("Output file info must be set before writing messages.");
        }

        if (_formatter == null)
        {
            throw new InvalidOperationException("Message writer must be initialized before use.");
        }

        try
        {
            // TODO: Maybe simplify and get FileStream from FileInfo directly
            await using var fileStream = _outputFileInfo.OpenWrite();
            await using var writer = new StreamWriter(fileStream);

            if (_outputFormat is OutputFormat.Json)
            {
                await writer.WriteLineAsync("[");
            }

            var isFirstMessage = true;

            await foreach (var message in messageChannel.Reader.ReadAllAsync())
            {
                _logger.LogDebug("[*] Start processing message #{DeliveryTag}...", message.DeliveryTag);

                if (!isFirstMessage)
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
                isFirstMessage = false;
                
                var formattedMessage = _formatter.FormatMessage(message);
                await writer.WriteLineAsync(formattedMessage);

                await ackChannel.Writer.WriteAsync((message.DeliveryTag, ackMode));
                _logger.LogDebug("[*] Message #{DeliveryTag} processed successfully", message.DeliveryTag);
            }

            if (_outputFormat is OutputFormat.Json)
            {
                await writer.WriteLineAsync("]");
            }

            await writer.FlushAsync();
        }
        catch (Exception)
        {
            _logger.LogError("[x] Failed to write messages to file '{FileName}'", _outputFileInfo.FullName);
        }
        finally
        {
            ackChannel.Writer.TryComplete();
            _logger.LogDebug("[*] Done!");
        }
    }
}