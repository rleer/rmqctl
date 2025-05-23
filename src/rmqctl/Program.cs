﻿using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using rmqctl;
using rmqctl.Commands;
using rmqctl.Configuration;
using rmqctl.Services;


var builder = Host.CreateApplicationBuilder();

// Register configuration settings
builder.Services.Configure<RabbitMqConfig>(
    builder.Configuration.GetSection(nameof(RabbitMqConfig)));
builder.Services.Configure<FileConfig>(
    builder.Configuration.GetSection(nameof(FileConfig)));
builder.Services.AddSingleton<DaemonConfig>();

// Register services in the DI container
builder.Services.AddSingleton<IRabbitChannelFactory, RabbitChannelFactory>();
builder.Services.AddSingleton<IPublishService, PublishService>();
builder.Services.AddSingleton<IConsumeService, ConsumeService>();

// Register command handlers
builder.Services.AddSingleton<ICommandHandler, PublishCommandHandler>();
builder.Services.AddSingleton<ICommandHandler, ConsumeCommandHandler>();

// Register background services
builder.Services.AddHostedService<ContinuousConsumerService>();

// Build host to create service provider and configuration
var host = builder.Build();

// Configure commands
var commandLineBuilder = new CommandLineBuilder(host);
commandLineBuilder.ConfigureCommands();

// Run the command line application
var exitCode = await commandLineBuilder.RunAsync(args);

// Check if daemon mode was requested
var daemonConfig = host.Services.GetRequiredService<DaemonConfig>();
if (daemonConfig.IsDaemonMode)
{
    // Run the host in daemon mode
    await host.RunAsync();
    return 0;
}

return exitCode;