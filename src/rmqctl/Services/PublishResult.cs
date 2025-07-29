using RabbitMQ.Client;
using rmqctl.Utilities;

namespace rmqctl.Services;

public record PublishResult(
    string MessageId,
    long MessageLength,
    AmqpTimestamp AmqTime)
{
    public string MessageSize => OutputUtilities.ToSizeString(MessageLength);
    public DateTimeOffset Timestamp => DateTimeOffset.FromUnixTimeSeconds(AmqTime.UnixTime);
}