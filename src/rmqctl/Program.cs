using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using rmqctl;
using rmqctl.Commands;
using rmqctl.Configuration;
using rmqctl.MessageFormatter;
using rmqctl.MessageWriter;
using rmqctl.Services;

// Parse command line arguments early to get configuration file path
string? customConfigPath = null;

// Simple command line argument parsing to find the custom config path
for (var i = 0; i < args.Length; i++)
{
    if (args[i] == "--config")
    {
        if (i + 1 < args.Length)
        {
            customConfigPath = args[i + 1];
            break;
        }
    }
}

var builder = Host.CreateApplicationBuilder();

// Clear default configuration sources and build custom configuration
builder.Configuration.Sources.Clear();

// Build configuration in priority order (lowest to highest priority):
// 1. Default TOML config (fallback)
// 2. System-wide TOML config
// 3. User TOML config
// 4. Custom TOML config (if specified via --config)
// 5. Environment variables

// Create default user config if it does not exist
ConfigurationPathHelper.CreateDefaultUserConfigIfNotExists();

// Add TOML configuration sources in priority order
var systemConfigPath = ConfigurationPathHelper.GetSystemConfigFilePath();
if (File.Exists(systemConfigPath))
{
    builder.Configuration.AddTomlConfig(systemConfigPath);
}

var userConfigPath = ConfigurationPathHelper.GetUserConfigFilePath();
if (File.Exists(userConfigPath))
{
    builder.Configuration.AddTomlConfig(userConfigPath);
}

// Add custom configuration file if specified
// TODO: Prompt user if file does not exist and fallback to default config is used
if (File.Exists(customConfigPath))
{
    builder.Configuration.AddTomlConfig(customConfigPath);
}
else
{
    // TODO: User logger and add user-friendly error message
    Console.Error.WriteLine("Configuration file not found. Using default configuration.");
}

// Add environment variables as the highest priority configuration source
builder.Configuration.AddEnvironmentVariables("RMQCTL_");

// Register configuration settings - avoid using IOptions to keep it simple
var rabbitMqConfig = new RabbitMqConfig();
builder.Configuration.GetSection(RabbitMqConfig.RabbitMqConfigName).Bind(rabbitMqConfig);
builder.Services.AddSingleton(rabbitMqConfig);

var fileConfig = new FileConfig();
builder.Configuration.GetSection(nameof(FileConfig)).Bind(fileConfig);
builder.Services.AddSingleton(fileConfig);


// Register services in the DI container
builder.Services.AddSingleton<IRabbitChannelFactory, RabbitChannelFactory>();
builder.Services.AddSingleton<IPublishService, PublishService>();
builder.Services.AddSingleton<IConsumeService, ConsumeService>();

// Register message formatters
builder.Services.AddSingleton<IMessageFormatter, TextMessageFormatter>();
builder.Services.AddSingleton<IMessageFormatter, JsonMessageFormatter>();
builder.Services.AddSingleton<IMessageFormatterFactory, MessageFormatterFactory>();

// Register message writers
builder.Services.AddSingleton<IMessageWriter, ConsoleMessageWriter>();
builder.Services.AddSingleton<IMessageWriter, SingleFileMessageWriter>();
builder.Services.AddSingleton<IMessageWriter, RotatingFileMessageWriter>();
builder.Services.AddSingleton<IMessageWriterFactory, MessageWriterFactory>();

// Register command handlers
builder.Services.AddSingleton<ICommandHandler, PublishCommandHandler>();
builder.Services.AddSingleton<ICommandHandler, ConsumeCommandHandler>();
builder.Services.AddSingleton<ICommandHandler, ConfigCommandHandler>();

// Build host to create service provider and configuration
var host = builder.Build();

var logger = host.Services.GetRequiredService<ILogger<Program>>();

try
{
    // Configure commands
    var commandLineBuilder = new CommandLineBuilder(host);
    commandLineBuilder.ConfigureCommands();
    
    // Run the command line application
    var exitCode = await commandLineBuilder.RunAsync(args);

    return exitCode; 
}
catch (Exception e)
{
    logger.LogError(e, "Application terminated unexpectedly");
    return 1;
}
