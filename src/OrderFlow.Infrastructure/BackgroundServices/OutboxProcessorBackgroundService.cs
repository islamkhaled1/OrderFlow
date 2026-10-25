using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderFlow.Infrastructure.Observability;
using OrderFlow.Infrastructure.Outbox;

namespace OrderFlow.Infrastructure.BackgroundServices;

/// <summary>
/// Polls the transactional outbox. Each iteration creates its own DI scope (DbContext is scoped).
/// When a batch was full it loops immediately; otherwise it sleeps for the poll interval.
/// Multiple instances of this worker (or API replicas) are safe: claims are exclusive.
/// </summary>
public class OutboxProcessorBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<OutboxOptions> _options;
    private readonly InfrastructureMetrics _metrics;
    private readonly ILogger<OutboxProcessorBackgroundService> _logger;

    public OutboxProcessorBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<OutboxOptions> options,
        InfrastructureMetrics metrics,
        ILogger<OutboxProcessorBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _metrics = metrics;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var poll = TimeSpan.FromSeconds(Math.Max(1, _options.Value.PollIntervalSeconds));
        _logger.LogInformation("Outbox processor started (poll interval {Interval}).", poll);

        while (!stoppingToken.IsCancellationRequested)
        {
            var handled = 0;
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var processor = scope.ServiceProvider.GetRequiredService<OutboxProcessor>();
                handled = await processor.ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Infrastructure-level failure (e.g. SQL Server down). Messages stay safely in the table.
                _metrics.WorkerFailure("outbox");
                _logger.LogError(ex, "Outbox processing cycle failed; will retry after the poll interval.");
            }

            if (handled >= _options.Value.BatchSize) continue; // backlog: keep draining

            try
            {
                await Task.Delay(poll, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }

        _logger.LogInformation("Outbox processor is stopping gracefully.");
    }
}
