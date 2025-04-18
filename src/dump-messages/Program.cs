using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace dump_messages;

internal class Program
{
    public static async Task Main()
    {
        // Build configuration
        var configuration = new ConfigurationBuilder()
            .SetBasePath(Directory.GetCurrentDirectory())
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development"}.json", optional: true)
            .AddEnvironmentVariables()
            .Build();
        
        // Set up DI
        var services = new ServiceCollection();
        ConfigureServices(services, configuration);

        var serviceProvider = services.BuildServiceProvider();
        var logger = serviceProvider.GetRequiredService<ILogger<Program>>();

        try
        {
            logger.LogInformation("Attempting to connect to RabbitMQ...");

            var rabbitConfig = configuration.GetSection("RabbitMQConfig").Get<RabbitMQConfig>()
                ?? throw new InvalidOperationException("RabbitMQConfig is missing");
            
            var factory = new ConnectionFactory
            {
                HostName = rabbitConfig.Host,
                VirtualHost = rabbitConfig.VirtualHost,
                Port = rabbitConfig.Port,
                UserName = rabbitConfig.User,
                Password = rabbitConfig.Password,
            };

            await using var connection = await factory.CreateConnectionAsync();
            logger.LogInformation("Successfully connected to RabbitMQ.");

            await using var channel = await connection.CreateChannelAsync();
            logger.LogInformation("Successfully created a channel.");
           
            const string message = "Hello, RabbitMQ!";
            var body = System.Text.Encoding.UTF8.GetBytes(message);
            await channel.BasicPublishAsync(
                rabbitConfig.Exchange,
                "Logging",
                body);
            
            logger.LogInformation("Message sent: {Message}", message);
            
            
            var consumer = new AsyncEventingBasicConsumer(channel);
            consumer.ReceivedAsync += (sender, @event) =>
            {
                var receivedBody = @event.Body.ToArray();
                var receivedMessage = System.Text.Encoding.UTF8.GetString(receivedBody);
                logger.LogInformation("Received message: {Message}", receivedMessage);
                return Task.CompletedTask;
            };
            await channel.BasicConsumeAsync(
                "BO.Logging",
                true,
                consumer);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while connecting to RabbitMQ.");
        }
    }

    public static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        services.AddLogging(configure => configure.AddConsole())
            .Configure<LoggerFilterOptions>(options => options.MinLevel = LogLevel.Debug);

        services.AddSingleton(configuration);
    }
}