using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using rmqctl.Configuration;
using rmqctl.Models;

namespace rmqctl.MessageWriter;

public class SingleFileMessageWriter : IMessageWriter
{
    private readonly ILogger<SingleFileMessageWriter> _logger;
    private readonly FileConfig _fileConfig;
    private FileInfo? _outputFileInfo;

    public SingleFileMessageWriter(ILogger<SingleFileMessageWriter> logger, IOptions<FileConfig> fileConfig)
    {
        _logger = logger;
        _fileConfig = fileConfig.Value;
    }

    public IMessageWriter Initialize(FileInfo? outputFileInfo)
    {
        _outputFileInfo = outputFileInfo;
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

        try
        {
            // TODO: Maybe simplify and get FileStream from FileInfo directly
            await using var fileStream = _outputFileInfo.OpenWrite();
            await using var writer = new StreamWriter(fileStream);
            
            await foreach (var message in messageChannel.Reader.ReadAllAsync())
            {
                _logger.LogDebug("[*] Start processing message #{DeliveryTag}...", message.DeliveryTag);
                await writer.WriteLineAsync(message.ToString());
                await writer.WriteLineAsync(_fileConfig.MessageDelimiter);

                await ackChannel.Writer.WriteAsync((message.DeliveryTag, ackMode));
                _logger.LogDebug("[*] Message #{DeliveryTag} processed successfully", message.DeliveryTag);
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