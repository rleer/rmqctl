using RabbitMQ.Client;

namespace rmqctl.Models;

public record RabbitMessage(
    string Body,
    ulong DeliveryTag,
    IReadOnlyBasicProperties? Props,
    bool Redelivered
);
