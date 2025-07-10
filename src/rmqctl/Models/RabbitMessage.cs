using RabbitMQ.Client;
using rmqctl.Utilities;

namespace rmqctl.Models;

public class RabbitMessage
{
    public string Body { get; set; }
    public ulong DeliveryTag { get; set; }
    public IReadOnlyBasicProperties? Props { get; set; }
    public bool Redelivered { get; set; }

    public RabbitMessage(string body, ulong deliveryTag, IReadOnlyBasicProperties? props, bool redelivered)
    {
        Body = body;
        DeliveryTag = deliveryTag;
        Props = props;
        Redelivered = redelivered;
    }

    public override string ToString()
    {
        return "DeliveryTag: " + DeliveryTag + "\n" +
               "Redelivered: " + Redelivered + "\n" +
               MessageFormater.FormatBasicProperties(Props) +
               "Body:\n" + Body;
    }
}