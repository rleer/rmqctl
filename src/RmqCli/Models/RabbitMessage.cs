using RabbitMQ.Client;

namespace RmqCli.Models;

public record RabbitMessage(
    string Body,
    ulong DeliveryTag,
    IReadOnlyBasicProperties? Props,
    bool Redelivered
);
