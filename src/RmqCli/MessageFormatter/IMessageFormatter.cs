using RmqCli.Models;

namespace RmqCli.MessageFormatter;

public interface IMessageFormatter
{
    string FormatMessage(RabbitMessage message);
    string FormatMessages(IEnumerable<RabbitMessage> messages);
}
