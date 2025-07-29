using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using rmqctl.Configuration;

namespace rmqctl.Services;

public interface IRabbitChannelFactory
{
    Task<IChannel> GetChannelAsync();
    Task<IChannel> GetChannelWithPublisherConfirmsAsync();
}

public class RabbitChannelFactory : IRabbitChannelFactory
{
    private readonly RabbitMqConfig _config;
    private readonly ILogger<RabbitChannelFactory> _logger;
    private readonly ConnectionFactory _connectionFactory;
    private IConnection? _connection;

    public RabbitChannelFactory(RabbitMqConfig rabbitMqConfig, ILogger<RabbitChannelFactory> logger)
    {
        _config = rabbitMqConfig;
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

    public async Task<IChannel> GetChannelWithPublisherConfirmsAsync()
    {
        var connection = await GetConnectionAsync();

        _logger.LogDebug("Acquiring RabbitMQ channel with publisher confirmations enabled");
        var channelOptions = new CreateChannelOptions(
            publisherConfirmationsEnabled: true,
            publisherConfirmationTrackingEnabled: true,
            outstandingPublisherConfirmationsRateLimiter: new ThrottlingRateLimiter(256)); // TODO: Should be configurable?
        var channel = await connection.CreateChannelAsync(channelOptions);

        // Register event handlers for channel events
        channel.BasicReturnAsync += OnChannelOnBasicReturnAsync;
        channel.ChannelShutdownAsync += OnChannelOnChannelShutdownAsync;

        return channel;
    }

    private async Task<IChannel> GetChannelAsync(IConnection connection)
    {
        _logger.LogDebug("Acquiring RabbitMQ channel");
        var channel = await connection.CreateChannelAsync();

        // Register event handlers for channel events
        channel.BasicReturnAsync += OnChannelOnBasicReturnAsync;
        channel.ChannelShutdownAsync += OnChannelOnChannelShutdownAsync;

        return channel;
    }

    private async Task<IConnection> GetConnectionAsync()
    {
        if (_connection is { IsOpen: true })
        {
            _logger.LogDebug("Reusing existing RabbitMQ connection");
            return _connection;
        }

        _logger.LogDebug("Connecting to RabbitMQ, host={Host}, port={Port}, vhost={VirtualHost}, client={ClientName}",
            _config.Host, _config.Port, _config.VirtualHost, _config.ClientName);
        _connection = await _connectionFactory.CreateConnectionAsync();

        _connection.ConnectionShutdownAsync += (_, args) =>
        {
            _logger.LogWarning("RabbitMQ connection shut down: {Reason}", args.ReplyText);
            return Task.CompletedTask;
        };

        return _connection;
    }

    private Task OnChannelOnChannelShutdownAsync(object _, ShutdownEventArgs @event)
    {
        _logger.LogInformation("RabbitMQ channel shut down: {ReplyText}", @event.ReplyText);
        return Task.CompletedTask;
    }

    private Task OnChannelOnBasicReturnAsync(object _, BasicReturnEventArgs @event)
    {
        _logger.LogWarning("Message returned: {ReplyCode} - {ReplyText}", @event.ReplyCode, @event.ReplyText);
        return Task.CompletedTask;
    }
}