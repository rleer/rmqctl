using System.Threading.Channels;
using RmqCli.Models;

namespace RmqCli.MessageWriter;

public interface IMessageWriter
{
    Task WriteMessageAsync(
        Channel<RabbitMessage> messageChannel,
        Channel<(ulong deliveryTag, AckModes ackMode)> ackChannel,
        AckModes ackMode);

    IMessageWriter Initialize(FileInfo? outputFileInfo, OutputFormat outputFormat = OutputFormat.Plain);
}