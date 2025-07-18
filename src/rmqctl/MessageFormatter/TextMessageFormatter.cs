using System.Text;
using RabbitMQ.Client;
using rmqctl.Models;

namespace rmqctl.MessageFormatter;

public class TextMessageFormatter : IMessageFormatter
{
    public string FormatMessage(RabbitMessage message)
    {
        return "DeliveryTag: " + message.DeliveryTag + "\n" +
               "Redelivered: " + message.Redelivered + "\n" +
               FormatBasicProperties(message.Props) +
               "Body:\n" + message.Body;
    }
    
    public string FormatMessages(IEnumerable<RabbitMessage> messages)
    {
        return string.Join("\n", messages.Select(FormatMessage));
    }

    private static string FormatBasicProperties(IReadOnlyBasicProperties? messageProps)
    {
        if (messageProps is null)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
       
        if (messageProps.IsTypePresent())
            sb.AppendLine($"Type: {messageProps.Type}");
        if (messageProps.IsMessageIdPresent())
            sb.AppendLine($"MessageId: {messageProps.MessageId}");
        if (messageProps.IsAppIdPresent())
            sb.AppendLine($"AppId: {messageProps.AppId}");
        if (messageProps.IsClusterIdPresent())
            sb.AppendLine($"ClusterId: {messageProps.ClusterId}");
        if (messageProps.IsContentTypePresent())
            sb.AppendLine($"ContentType: {messageProps.ContentType}");
        if (messageProps.IsContentEncodingPresent())
            sb.AppendLine($"ContentEncoding: {messageProps.ContentEncoding}");
        if (messageProps.IsCorrelationIdPresent())
            sb.AppendLine($"CorrelationId: {messageProps.CorrelationId}");
        if (messageProps.IsDeliveryModePresent())
            sb.AppendLine($"DeliveryMode: {messageProps.DeliveryMode}");
        if (messageProps.IsExpirationPresent())
            sb.AppendLine($"Expiration: {messageProps.Expiration}");
        if (messageProps.IsPriorityPresent())
            sb.AppendLine($"Priority: {messageProps.Priority}");
        if (messageProps.IsReplyToPresent())
            sb.AppendLine($"ReplyTo: {messageProps.ReplyTo}");
        if (messageProps.IsTimestampPresent())
        {
            var timestamp = DateTimeOffset.FromUnixTimeSeconds(messageProps.Timestamp.UnixTime);
            sb.AppendLine($"Timestamp: {timestamp:yyyy-MM-dd HH:mm:ss zzz}");
        }

        // Format headers if present
        if (messageProps.IsHeadersPresent())
            sb.AppendLine(FormatHeaders(messageProps.Headers));
        
        return sb.ToString();
    }

    private static string FormatHeaders(IDictionary<string, object?>? headers)
    {
        if (headers is null || headers.Count == 0)
        {
            return "Headers: null";
        }

        var sb = new StringBuilder();
        sb.AppendLine("Headers:");
        foreach (var header in headers)
        {
            sb.AppendLine($"  {header.Key}: {FormatValue(header.Value)}");
        }
        return sb.ToString().TrimEnd();
    }
    
    private static string FormatValue(object? value, int indentationLevel = 1)
    {
        switch (value)
        {
            case null:
                return "null";
            case byte[] bytes:
                try
                {
                    var strValue = Encoding.UTF8.GetString(bytes);
                    if (strValue.Any(c => char.IsControl(c) && c != '\r' && c != '\n' && c != '\t'))
                    {
                        return $"byte[{bytes.Length}]";
                    }

                    return strValue;
                }
                catch
                {
                    return $"byte[{bytes.Length}]";
                }
            case AmqpTimestamp timestamp:
            {
                var dateTime = DateTimeOffset.FromUnixTimeSeconds(timestamp.UnixTime);
                return $"{dateTime:yyyy-MM-dd HH:mm:ss zzz}";
            }
            case IEnumerable<object> enumerable when value is not string:
            {
                var sb = new StringBuilder();
                sb.AppendLine("[");
                foreach (var item in enumerable)
                {
                    sb.AppendLine($"{new string(' ', (indentationLevel + 1) * 2 - 2)}- {FormatValue(item, indentationLevel + 1).TrimStart()}");
                }
                sb.AppendLine($"{new string(' ', indentationLevel)} ]");
                return sb.ToString().TrimEnd();
            }
            case IDictionary<string, object> dict:
            {
                var sb = new StringBuilder();
                foreach (var pair in dict)
                {
                    sb.AppendLine($"{new string(' ', indentationLevel * 2)}{pair.Key}: {FormatValue(pair.Value, indentationLevel + 1)}");
                }

                return sb.ToString().TrimEnd();
            }
            default:
                return value.ToString() ?? "null";
        }
    }
}