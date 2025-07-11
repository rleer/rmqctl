using System.Threading.Channels;
using rmqctl.Models;

namespace rmqctl.MessageWriter;

public interface IMessageWriter
{
    Task WriteMessageAsync(
        Channel<RabbitMessage> messageChannel,
        Channel<(ulong deliveryTag, AckModes ackMode)> ackChannel,
        AckModes ackMode
    );
    
    IMessageWriter Initialize(FileInfo? outputFileInfo);
}