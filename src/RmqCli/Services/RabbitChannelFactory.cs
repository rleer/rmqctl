using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using RabbitMQ.Client.Exceptions;
using RmqCli.Configuration;

namespace RmqCli.Services;

public interface IRabbitChannelFactory
{
    Task<IChannel> GetChannelAsync();
    Task<IChannel> GetChannelWithPublisherConfirmsAsync();
    Task CloseConnectionAsync();
}

public class RabbitChannelFactory : IRabbitChannelFactory
{
    private readonly RabbitMqConfig _config;
    private readonly ILogger<RabbitChannelFactory> _logger;
    private readonly ConnectionFactory _connectionFactory;
    private readonly ICliOutputService _output;
    
    private IConnection? _connection;

    public RabbitChannelFactory(RabbitMqConfig rabbitMqConfig, ILogger<RabbitChannelFactory> logger, ICliOutputService output)
    {
        _config = rabbitMqConfig;
        _logger = logger;
        _output = output;
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
        try
        {
            var connection = await GetConnectionAsync();
            return await GetChannelAsync(connection);
        }
        catch (Exception ex)
        {
            HandleConnectionException(ex);
            throw;
        }
    }

    public async Task<IChannel> GetChannelWithPublisherConfirmsAsync()
    {
        try
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
        catch (Exception ex)
        {
            HandleConnectionException(ex);
            throw;
        }
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
            if (args.ReplyCode == 200)
            {
                _logger.LogDebug("RabbitMQ connection shut down: {Reason} ({ReasonCode})", args.ReplyText, args.ReplyCode);
                return Task.CompletedTask;
            }
            _logger.LogWarning("RabbitMQ connection shut down due to a failure: {Reason} ({ReasonCode})", args.ReplyText, args.ReplyCode);
            return Task.CompletedTask;
        };

        return _connection;
    }

    private void HandleConnectionException(Exception ex)
    {
        // Check the entire exception chain to find the most specific error
        var specificException = GetMostSpecificException(ex);
        
        switch (specificException)
        {
            case OperationInterruptedException opEx when opEx.ShutdownReason?.ReplyCode == 530:
                var errorText = opEx.ShutdownReason.ReplyText;
                
                if (errorText.Contains("not found"))
                {
                    _logger.LogError("RabbitMQ connection failed: Virtual host '{VirtualHost}' not found", _config.VirtualHost);
                    _output.ShowError($"Connection failed: Virtual host '{_config.VirtualHost}' not found");
                }
                else if (errorText.Contains("refused") || errorText.Contains("access") && errorText.Contains("denied"))
                {
                    _logger.LogError("RabbitMQ connection failed: Access denied for user '{User}' to virtual host '{VirtualHost}'", _config.User, _config.VirtualHost);
                    _output.ShowError($"Connection failed: Access denied for user '{_config.User}' to virtual host '{_config.VirtualHost}'");
                }
                else
                {
                    // Fallback for other 530 errors - use the cleaned error message
                    _logger.LogError("RabbitMQ connection failed: {Reason} ({Code})", errorText, opEx.ShutdownReason.ReplyCode);
                    _output.ShowError("Connection failed", errorText);
                }
                break;
            case AuthenticationFailureException:
                _logger.LogError("RabbitMQ connection failed: Authentication failed for user '{User}'", _config.User);
                _output.ShowError($"Connection failed: Authentication failed for user '{_config.User}'");
                break;
            case ConnectFailureException:
                _logger.LogError("RabbitMQ connection failed: Could not connect to {Host}:{Port}", _config.Host, _config.Port);
                _output.ShowError($"Connection failed: Could not connect to {_config.Host}:{_config.Port}");
                break;
            case BrokerUnreachableException:
                _logger.LogError("RabbitMQ connection failed: Broker unreachable at {Host}:{Port}", _config.Host, _config.Port);
                _output.ShowError($"Connection failed: RabbitMQ broker unreachable at {_config.Host}:{_config.Port}");
                break;
            case OperationInterruptedException opEx:
                _logger.LogError("RabbitMQ operation interrupted: {ReplyText} (Code: {ReplyCode})", 
                    opEx.ShutdownReason?.ReplyText ?? "Unknown", opEx.ShutdownReason?.ReplyCode ?? 0);
                _output.ShowError("RabbitMQ operation interrupted",opEx.ShutdownReason?.ReplyText ?? ex.Message);
                break;
            default:
                _logger.LogError("Unexpected RabbitMQ connection error"); // exception will be logged in service
                _output.ShowError("Connection failed", ex.Message);
                break;
        }
    }

    /// <summary>
    /// Traverses the exception chain to find the most specific RabbitMQ exception.
    /// This handles cases where specific exceptions are wrapped inside generic ones.
    /// </summary>
    private static Exception GetMostSpecificException(Exception ex)
    {
        var current = ex;
        Exception? mostSpecific = null;
        
        // Define exception priority order (most specific first)
        var priorityOrder = new[]
        {
            typeof(AuthenticationFailureException),
            typeof(OperationInterruptedException),
            typeof(ConnectFailureException),
            typeof(BrokerUnreachableException)
        };
        
        // Traverse the exception chain
        while (current != null)
        {
            // Check if this exception type has higher priority than what we've found
            var currentPriority = Array.IndexOf(priorityOrder, current.GetType());
            if (currentPriority >= 0)
            {
                var existingPriority = mostSpecific != null ? Array.IndexOf(priorityOrder, mostSpecific.GetType()) : int.MaxValue;
                if (currentPriority < existingPriority)
                {
                    mostSpecific = current;
                }
            }
            
            current = current.InnerException;
        }
        
        // Return the most specific exception found, or the original if none matched our priority list
        return mostSpecific ?? ex;
    }

    public async Task CloseConnectionAsync()
    {
        if (_connection != null)
        {
            await _connection.CloseAsync();
            await _connection.DisposeAsync();
        }
    }

    private Task OnChannelOnChannelShutdownAsync(object _, ShutdownEventArgs @event)
    {
        _logger.LogInformation("RabbitMQ channel shut down: {ReplyText} ({ReplyCode})", @event.ReplyText, @event.ReplyCode);
        return Task.CompletedTask;
    }

    private Task OnChannelOnBasicReturnAsync(object _, BasicReturnEventArgs @event)
    {
        _logger.LogWarning("Message returned: {ReplyText} ({ReplyCode})", @event.ReplyText, @event.ReplyCode);
        return Task.CompletedTask;
    }
}