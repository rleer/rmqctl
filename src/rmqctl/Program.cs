using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using rmqctl;
using rmqctl.Commands;
using rmqctl.Configuration;
using rmqctl.MessageFormatter;
using rmqctl.MessageWriter;
using rmqctl.Services;


var builder = Host.CreateApplicationBuilder();

// Register configuration settings
builder.Services.Configure<RabbitMqConfig>(
    builder.Configuration.GetSection(nameof(RabbitMqConfig)));
builder.Services.Configure<FileConfig>(
    builder.Configuration.GetSection(nameof(FileConfig)));

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
