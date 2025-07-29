using System.Text;
using System.Text.Json;
using RabbitMQ.Client;
using RmqCli.Models;

namespace RmqCli.MessageFormatter;

public class JsonMessageFormatter : IMessageFormatter
{
    public string FormatMessage(RabbitMessage message)
    {
        var messageDto = CreateMessageDto(message);
        return JsonSerializer.Serialize(messageDto, JsonSerializationContext.Default.MessageDto);
    }

    public string FormatMessages(IEnumerable<RabbitMessage> messages)
    {
        var messageDtos = messages.Select(CreateMessageDto).ToArray();
        return JsonSerializer.Serialize(messageDtos, JsonSerializationContext.Default.MessageDtoArray);
    }

    private MessageDto CreateMessageDto(RabbitMessage message)
    {
        Dictionary<string, object>? properties = null;
        if (message.Props != null)
        {
            var props = CreatePropertiesObject(message.Props);
            if (props.Count > 0)
            {
                properties = props;
            }
        }

        return new MessageDto(
            message.DeliveryTag,
            message.Redelivered,
            message.Body,
            properties
        );
    }

    private Dictionary<string, object> CreatePropertiesObject(IReadOnlyBasicProperties props)
    {
        var properties = new Dictionary<string, object>();

        if (props.IsTypePresent())
            properties["type"] = props.Type ?? string.Empty;
        if (props.IsMessageIdPresent())
            properties["messageId"] = props.MessageId ?? string.Empty;
        if (props.IsAppIdPresent())
            properties["appId"] = props.AppId ?? string.Empty;
        if (props.IsClusterIdPresent())
            properties["clusterId"] = props.ClusterId ?? string.Empty;
        if (props.IsContentTypePresent())
            properties["contentType"] = props.ContentType ?? string.Empty;
        if (props.IsContentEncodingPresent())
            properties["contentEncoding"] = props.ContentEncoding ?? string.Empty;
        if (props.IsCorrelationIdPresent())
            properties["correlationId"] = props.CorrelationId ?? string.Empty;
        if (props.IsDeliveryModePresent())
            properties["deliveryMode"] = props.DeliveryMode;
        if (props.IsExpirationPresent())
            properties["expiration"] = props.Expiration ?? string.Empty;
        if (props.IsPriorityPresent())
            properties["priority"] = props.Priority;
        if (props.IsReplyToPresent())
            properties["replyTo"] = props.ReplyTo ?? string.Empty;
        if (props.IsTimestampPresent())
        {
            var timestamp = DateTimeOffset.FromUnixTimeSeconds(props.Timestamp.UnixTime);
            properties["timestamp"] = timestamp.ToString("yyyy-MM-dd HH:mm:ss zzz");
        }

        if (props.IsHeadersPresent() && props.Headers != null)
        {
            var headers = ConvertHeaders(props.Headers);
            if (headers.Count > 0)
            {
                properties["headers"] = headers;
            }
        }

        return properties;
    }

    private Dictionary<string, object> ConvertHeaders(IDictionary<string, object?> headers)
    {
        var convertedHeaders = new Dictionary<string, object>();

        foreach (var header in headers)
        {
            if (header.Value != null)
            {
                convertedHeaders[header.Key] = ConvertValue(header.Value);
            }
        }

        return convertedHeaders;
    }

    private object ConvertValue(object value)
    {
        return value switch
        {
            null => "null",
            byte[] bytes => ConvertByteArray(bytes),
            AmqpTimestamp timestamp => DateTimeOffset.FromUnixTimeSeconds(timestamp.UnixTime).ToString("yyyy-MM-dd HH:mm:ss zzz"),
            IEnumerable<object> enumerable when value is not string => enumerable.Select(ConvertValue).ToArray(),
            IDictionary<string, object> dict => dict.ToDictionary(pair => pair.Key, pair => ConvertValue(pair.Value)),
            _ => value
        };
    }

    private object ConvertByteArray(byte[] bytes)
    {
        try
        {
            var strValue = Encoding.UTF8.GetString(bytes);
            // Check if the string contains control characters (except common ones)
            if (strValue.Any(c => char.IsControl(c) && c != '\r' && c != '\n' && c != '\t'))
            {
                return $"<binary data: {bytes.Length} bytes>";
            }
            return strValue;
        }
        catch
        {
            return $"<binary data: {bytes.Length} bytes>";
        }
    }
}
