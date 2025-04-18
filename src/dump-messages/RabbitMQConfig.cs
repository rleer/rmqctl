namespace dump_messages;

public class RabbitMQConfig
{
    public required string Host { get; set; }
    public required string VirtualHost { get; set; }
    public int Port { get; set; }
    public required string User { get; set; }
    public required string Password { get; set; }
    public required string Exchange { get; set; }
}