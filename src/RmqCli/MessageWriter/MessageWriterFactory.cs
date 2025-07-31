using Microsoft.Extensions.Options;
using RmqCli.Configuration;
using RmqCli.Models;

namespace RmqCli.MessageWriter;

public interface IMessageWriterFactory
{
    IMessageWriter CreateWriter(FileInfo? outputFileInfo, int messageCount, OutputFormat outputFormat = OutputFormat.Plain);
}

public class MessageWriterFactory : IMessageWriterFactory
{
    private readonly IEnumerable<IMessageWriter> _writers;
    private readonly FileConfig _fileConfig;
    
    public MessageWriterFactory(FileConfig fileConfig, IEnumerable<IMessageWriter> writers)
    {
        _writers = writers;
        _fileConfig = fileConfig;
    }

    public IMessageWriter CreateWriter(FileInfo? outputFileInfo, int messageCount, OutputFormat outputFormat = OutputFormat.Plain)
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