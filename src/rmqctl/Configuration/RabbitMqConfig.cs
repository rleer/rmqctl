namespace rmqctl.Configuration;

public class RabbitMqConfig
{
    public required string Host { get; set; }
    public required string VirtualHost { get; set; }
    public required int Port { get; set; }
    public required string User { get; set; }
    public required string Password { get; set; }
    public required string Exchange { get; set; }
    public required string ClientName { get; set; }
}