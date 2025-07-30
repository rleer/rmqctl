using System.CommandLine;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RmqCli;
using RmqCli.Commands;
using RmqCli.Configuration;
using RmqCli.MessageFormatter;
using RmqCli.MessageWriter;
using RmqCli.Services;
using Spectre.Console;

// Create a minimal root command to parse global options first
var tempRootCommand = new RootCommand();
var configOption = new Option<string>("--config", "Path to the configuration file (TOML format)");
var verboseOption = new Option<bool>("--verbose", () => false, "Enable verbose logging");
var quietOption = new Option<bool>("--quiet", () => false, "Minimal output (errors only)");
var jsonOption = new Option<bool>("--json", () => false, "Structured JSON output to stdout");
var noColorOption = new Option<bool>("--no-color", () => false, "Disable colored output for dumb terminals");

tempRootCommand.AddGlobalOption(configOption);
tempRootCommand.AddGlobalOption(verboseOption);
tempRootCommand.AddGlobalOption(quietOption);
tempRootCommand.AddGlobalOption(jsonOption);
tempRootCommand.AddGlobalOption(noColorOption);

// Parse arguments to extract global option values
var parseResult = tempRootCommand.Parse(args);
var customConfigPath = parseResult.GetValueForOption(configOption);
var verboseLogging = parseResult.GetValueForOption(verboseOption);
var quietLogging = parseResult.GetValueForOption(quietOption);
var jsonOutput = parseResult.GetValueForOption(jsonOption);
var noColor = parseResult.GetValueForOption(noColorOption);

// TODO: Replace with manual DI container and configuration setup to avoid potential overhead of using Host
var builder = Host.CreateApplicationBuilder();

// Clear default configuration sources and build custom configuration
builder.Configuration.Sources.Clear();

// Add custom configuration sources in priority order
builder.Configuration.AddRmqConfig(customConfigPath);

builder.Logging.ClearProviders();

var logLevel = verboseLogging ? LogLevel.Debug : LogLevel.None;
builder.Logging.AddConsole(options => { options.LogToStandardErrorThreshold = LogLevel.Trace; })
    .AddFilter("Microsoft", LogLevel.Warning)
    .AddFilter("System", LogLevel.Warning)
    .SetMinimumLevel(logLevel);

builder.Logging.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "HH:mm:ss ";
    options.IncludeScopes = false;
});

// Register configuration settings - avoid using IOptions to keep it simple
var rabbitMqConfig = new RabbitMqConfig();
builder.Configuration.GetSection(RabbitMqConfig.RabbitMqConfigName).Bind(rabbitMqConfig);
builder.Services.AddSingleton(rabbitMqConfig);

var fileConfig = new FileConfig();
builder.Configuration.GetSection(nameof(FileConfig)).Bind(fileConfig);
builder.Services.AddSingleton(fileConfig);

var cliConfig = new CliConfig
{
    JsonOutput = jsonOutput,
    Quiet = quietLogging,
    Verbose = verboseLogging,
    NoColor = noColor
};
builder.Services.AddSingleton(cliConfig);

// Register services in the DI container
builder.Services.AddSingleton<IRabbitChannelFactory, RabbitChannelFactory>();
builder.Services.AddSingleton<IPublishService, PublishService>();
builder.Services.AddSingleton<IConsumeService, ConsumeService>();
builder.Services.AddSingleton<ICliOutputService, CliOutputService>();

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
    // Configure commands with the properly configured host
    var commandLineBuilder = new CommandLineBuilder(host);
    commandLineBuilder.ConfigureCommands();

    // Run the command line application
    var exitCode = await commandLineBuilder.RunAsync(args);

    return exitCode;
}
catch (Exception e)
{
    AnsiConsole.MarkupLineInterpolated($"[indianred1]⚠ An error occurred: {e.Message}[/]");
    logger.LogError(e, "Application terminated unexpectedly");
    return 1;
}