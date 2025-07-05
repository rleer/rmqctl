using System.Text;
using RabbitMQ.Client;

namespace rmqctl.Utilities;

public static class MessageFormater
{
    public static string FormatBasicProperties(ReadOnlyBasicProperties? messageProps)
    {
        if (messageProps is null)
        {
            return "null";
        }

        var sb = new StringBuilder();
        
        // Add all standard properties
        // sb.AppendLine($"  ContentType: {messageProps.ContentType ?? "null"}");
        // sb.AppendLine($"  ContentEncoding: {messageProps.ContentEncoding ?? "null"}");
        // sb.AppendLine($"  DeliveryMode: {messageProps.DeliveryMode}");;
        // sb.AppendLine($"  Priority: {messageProps.Priority}");
        // sb.AppendLine($"  CorrelationId: {messageProps?.CorrelationId ?? "null"}");
        // sb.AppendLine($"  ReplyTo: {messageProps?.ReplyTo ?? "null"}");
        // sb.AppendLine($"  Expiration: {messageProps?.Expiration ?? "null"}");
        // sb.AppendLine($"  MessageId: {messageProps?.MessageId ?? "null"}");

        // Format timestamp as readable datetime if available
        if (messageProps?.Timestamp.UnixTime > 0)
        {
            var timestamp = DateTimeOffset.FromUnixTimeSeconds(messageProps.Timestamp.UnixTime);
            sb.AppendLine($"  Timestamp: {timestamp:yyyy-MM-dd HH:mm:ss zzz}");
        }
        // else
        // {
        //     sb.AppendLine("  Timestamp: null");
        // }
        //
        // sb.AppendLine($"  Type: {messageProps?.Type ?? "null"}");
        // sb.AppendLine($"  UserId: {messageProps?.UserId ?? "null"}");
        // sb.AppendLine($"  AppId: {messageProps?.AppId ?? "null"}");
        // sb.AppendLine($"  ClusterId: {messageProps?.ClusterId ?? "null"}");

        // Format headers if present
        if (messageProps?.Headers?.Count > 0)
        {
            sb.AppendLine("  Headers:");
            foreach (var header in messageProps.Headers)
            {
                sb.AppendLine($"    {header.Key}: {header.Value}");
            }
        }
        else
        {
            sb.AppendLine("  Headers: null");
        }
        
        return sb.ToString();
    }
    
    public static string FormatHeaders(IDictionary<string, object?>? headers)
    {
        if (headers is null || headers.Count == 0)
        {
            return " null";
        }

        var sb = new StringBuilder();
        sb.AppendLine();
        foreach (var header in headers)
        {
            sb.AppendLine($"  {header.Key}: {FormatValue(header.Value)}");
        }
        return sb.ToString();
    }
    
    public static string FormatValue(object? value, int indentationLevel = 1)
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