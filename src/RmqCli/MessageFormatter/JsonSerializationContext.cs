using System.Text.Json.Serialization;
using RmqCli.Models;

namespace RmqCli.MessageFormatter;

public record MessageDto(
    ulong DeliveryTag,
    bool Redelivered,
    string Body,
    Dictionary<string, object>? Properties = null
);

public record MessageArrayDto(MessageDto[] Messages);

[JsonSerializable(typeof(MessageDto))]
[JsonSerializable(typeof(MessageDto[]))]
[JsonSerializable(typeof(Dictionary<string, object>))]
[JsonSerializable(typeof(string))]
[JsonSerializable(typeof(int))]
[JsonSerializable(typeof(byte))]
[JsonSerializable(typeof(bool))]
[JsonSerializable(typeof(object))]
[JsonSerializable(typeof(object[]))]
[JsonSerializable(typeof(RabbitMQ.Client.DeliveryModes))]
[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase
)]
public partial class JsonSerializationContext : JsonSerializerContext
{
}