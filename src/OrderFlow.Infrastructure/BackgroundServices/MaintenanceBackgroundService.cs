using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderFlow.Application.Interfaces;
using OrderFlow.Infrastructure.Observability;
using OrderFlow.Infrastructure.Services;

namespace OrderFlow.Infrastructure.BackgroundServices;

/// <summary>
/// Periodic housekeeping: (1) cancel stale pending orders and release their stock, (2) purge processed
/// outbox rows and expired idempotency keys. It deliberately does NOT complete orders: completion is an
/// explicit administrator action.
/// </summary>
public class MaintenanceBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<BackgroundJobOptions> _options;
    private readonly InfrastructureMetrics _metrics;
    private readonly ILogger<MaintenanceBackgroundService> _logger;

    public MaintenanceBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<BackgroundJobOptions> options,
        InfrastructureMetrics metrics,
        ILogger<MaintenanceBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _metrics = metrics;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(_options.Value.IntervalSeconds > 0 ? _options.Value.IntervalSeconds : 60);
        _logger.LogInformation("Maintenance worker started (interval {Interval}).", interval);

        using var timer = new PeriodicTimer(interval);
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            await RunCycleAsync(stoppingToken);
        }
    }

    public async Task RunCycleAsync(CancellationToken cancellationToken)
    {
        using var activity = InfrastructureTelemetry.ActivitySource.StartActivity("maintenance.cycle");
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var opts = _options.Value;

            if (opts.PendingOrderExpirationMinutes > 0)
            {
                var expiration = scope.ServiceProvider.GetRequiredService<IOrderExpirationService>();
                var cutoff = DateTime.UtcNow.AddMinutes(-opts.PendingOrderExpirationMinutes);
                var expired = await expiration.ExpireStaleOrdersAsync(cutoff, Math.Max(1, opts.ExpirationBatchSize), cancellationToken);
                if (expired > 0)
                {
                    _metrics.OrdersExpired(expired);
                    _logger.LogInformation("Expired {Count} stale pending order(s).", expired);
                }
            }

            var retention = scope.ServiceProvider.GetRequiredService<RetentionService>();
            await retention.PurgeProcessedOutboxAsync(cancellationToken);
            await retention.PurgeExpiredIdempotencyRecordsAsync(cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Maintenance cycle cancelled due to shutdown.");
        }
        catch (Exception ex)
        {
            _metrics.WorkerFailure("maintenance");
            _logger.LogError(ex, "Maintenance cycle failed.");
        }
    }
}
