namespace RmqCli.Configuration;

public class RabbitMqConfig
{
    public const string RabbitMqConfigName = "RabbitMq";
    public  string Host { get; set; } = "localhost";
    public  int Port { get; set; } = 5672;
    public  string VirtualHost { get; set; } = "/";
    public  string User { get; set; } = "guest";
    public  string Password { get; set; } = "guest";
    public  string Exchange { get; set; } = "amq.direct";
    public  string ClientName { get; set; } = "rmq-cli-client";
}