using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using rmqctl.Configuration;

namespace rmqctl.Services;

public interface IRabbitChannelFactory
{
    Task<IChannel> GetChannelAsync();
}

public class RabbitChannelFactory : IRabbitChannelFactory
{
    private readonly RabbitMqConfig _config;
    private readonly ILogger<RabbitChannelFactory> _logger;
    private readonly ConnectionFactory _connectionFactory;
    private IConnection? _connection;

    public RabbitChannelFactory(IOptions<RabbitMqConfig> options, ILogger<RabbitChannelFactory> logger)
    {
        _config = options.Value;
        _logger = logger;
        _connectionFactory = new ConnectionFactory
        {
            HostName = _config.Host,
            Port = _config.Port,
            UserName = _config.User,
            Password = _config.Password,
            VirtualHost = _config.VirtualHost,
            ClientProvidedName = _config.ClientName
        };
    }

    public async Task<IChannel> GetChannelAsync()
    {
        var connection = await GetConnectionAsync();
        return await GetChannelAsync(connection);
    }
    
    private async Task<IChannel> GetChannelAsync(IConnection connection)
    {
        _logger.LogInformation("Creating RabbitMQ channel...");
        var channel = await connection.CreateChannelAsync();
        
        channel.BasicReturnAsync += (sender, @event) =>
        {
            _logger.LogWarning("Message returned: {ReplyCode} - {ReplyText}", @event.ReplyCode, @event.ReplyText);
            return Task.CompletedTask;
        };
        
        return channel;
    }

    private async Task<IConnection> GetConnectionAsync()
    {
        if (_connection != null && _connection.IsOpen)
            return _connection;

        _logger.LogInformation("Connecting to RabbitMQ on {Host}:{Port}...", _config.Host, _config.Port);
        _connection = await _connectionFactory.CreateConnectionAsync();

        // Set up connection shutdown event handler
        _connection.ConnectionShutdownAsync += (sender, args) =>
        {
            _logger.LogWarning("RabbitMQ connection shut down: {Reason}", args.ReplyText);
            return Task.CompletedTask;
        };

        return _connection;
    }
}