using Microsoft.Extensions.Options;
using rmqctl.Configuration;
using rmqctl.Models;

namespace rmqctl.MessageWriter;

public interface IMessageWriterFactory
{
    IMessageWriter CreateWriter(FileInfo? outputFileInfo, int messageCount, OutputFormat outputFormat = OutputFormat.Text);
}

public class MessageWriterFactory : IMessageWriterFactory
{
    private readonly IEnumerable<IMessageWriter> _writers;
    private readonly FileConfig _fileConfig;
    
    public MessageWriterFactory(IOptions<FileConfig> fileConfig, IEnumerable<IMessageWriter> writers)
    {
        _writers = writers;
        _fileConfig = fileConfig.Value;
    }

    public IMessageWriter CreateWriter(FileInfo? outputFileInfo, int messageCount, OutputFormat outputFormat = OutputFormat.Text)
    {
        if (outputFileInfo is null)
        {
            return _writers.First(w => w is ConsoleMessageWriter)
                .Initialize(outputFileInfo, outputFormat);
        }

        if (messageCount != -1 && _fileConfig.MessagesPerFile >= messageCount)
        {
            return _writers.First(w => w is SingleFileMessageWriter)
                .Initialize(outputFileInfo, outputFormat);
        }
        return _writers.First(w => w is RotatingFileMessageWriter)
            .Initialize(outputFileInfo, outputFormat);
    }
}