using RabbitMQ.Client;
using RmqCli.Utilities;

namespace RmqCli.Services;

public record PublishResult(
    string MessageId,
    long MessageLength,
    AmqpTimestamp AmqTime)
{
    public string MessageSize => OutputUtilities.ToSizeString(MessageLength);
    public DateTimeOffset Timestamp => DateTimeOffset.FromUnixTimeSeconds(AmqTime.UnixTime);
}