using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using rmqctl.Configuration;

namespace rmqctl.Services;

public class ContinuousConsumerService : BackgroundService
{
    private readonly ILogger<ContinuousConsumerService> _logger;
    private readonly IConsumeService _consumeService;
    private readonly DaemonConfig _daemonConfig;
    private readonly IHostApplicationLifetime _hostLifetime;

    public ContinuousConsumerService(
        ILogger<ContinuousConsumerService> logger,
        IConsumeService consumeService,
        DaemonConfig daemonConfig,
        IHostApplicationLifetime hostLifetime
    )
    {
        _logger = logger;
        _consumeService = consumeService;
        _daemonConfig = daemonConfig;
        _hostLifetime = hostLifetime;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_daemonConfig.IsDaemonMode)
        {
            _logger.LogWarning("Daemon mode is not enabled. Exiting...");
            return;
        }

        if (_daemonConfig.Queue is null || _daemonConfig.AckMode is null)
        {
            _logger.LogWarning("Queue or acknowledge mode not provided to consume daemon. Exiting...");
            return;
        }

        await _consumeService.StartContinuousConsumptionAsync(
            _daemonConfig.Queue,
            _daemonConfig.AckMode.Value,
            _daemonConfig.MessageCount,
            stoppingToken
        );

        _logger.LogDebug("[x] Message consumer is done (cts: {CancellationToken}). Stopping application...", stoppingToken.IsCancellationRequested);
        _hostLifetime.StopApplication();
    }
    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stop Async cts: {CancellationToken}", cancellationToken.IsCancellationRequested);
        return base.StopAsync(cancellationToken);
    }
}