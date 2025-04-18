using dump_messages;
using dump_messages.Commands;
using dump_messages.Configuration;
using dump_messages.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;


var builder = Host.CreateApplicationBuilder();

// Configure services
builder.Services.Configure<RabbitMqConfig>(
    builder.Configuration.GetSection(nameof(RabbitMqConfig)));

// Register services in the DI container
builder.Services.AddSingleton<IRabbitChannelFactory, RabbitChannelFactory>();
builder.Services.AddSingleton<IPublishService, PublishService>();
builder.Services.AddSingleton<IDumpService, DumpService>();

// Register command handlers
builder.Services.AddSingleton<ICommandHandler, PublishCommandHandler>();
builder.Services.AddSingleton<ICommandHandler, ConsumeCommandHandler>();

// Build host to create service provider and configuration
var host = builder.Build();

// Configure commands
var commandLineBuilder = new CommandLineBuilder(host);
commandLineBuilder.ConfigureCommands();

// Run the command line application
var exitCode = await commandLineBuilder.RunAsync(args);

return exitCode;
