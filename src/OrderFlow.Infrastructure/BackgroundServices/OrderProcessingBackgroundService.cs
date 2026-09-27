using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderFlow.Application.Interfaces;

namespace OrderFlow.Infrastructure.BackgroundServices;

/// <summary>
/// Background worker that periodically:
/// 1. Processes Pending orders into Completed.
/// 2. Invalidates affected Redis cache entries.
/// 3. Refreshes the SQL Server Materialized View (OrderDashboardReadModels).
/// 
/// ARCHITECTURAL PRINCIPLE:
/// As a hosted singleton service, this worker NEVER injects scoped services
/// (such as DbContext or scoped repositories) directly into its constructor.
/// Instead, it uses IServiceScopeFactory to create an explicit scope per execution cycle.
/// </summary>
public class OrderProcessingBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IOptions<BackgroundJobOptions> _options;
    private readonly ILogger<OrderProcessingBackgroundService> _logger;
    private readonly Counter<long> _workerCyclesCounter;
    private readonly Counter<long> _workerProcessedCounter;

    public OrderProcessingBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<BackgroundJobOptions> options,
        ILogger<OrderProcessingBackgroundService> logger,
        IMeterFactory meterFactory)
    {
        _scopeFactory = scopeFactory;
        _options = options;
        _logger = logger;

        // Use the same meter name as OrderFlowMetrics for consistent Prometheus export
        var meter = meterFactory.Create("OrderFlow");
        _workerCyclesCounter = meter.CreateCounter<long>(
            "orderflow.worker.cycles",
            unit: "{cycles}",
            description: "Total number of background worker processing cycles");
        _workerProcessedCounter = meter.CreateCounter<long>(
            "orderflow.worker.orders_processed",
            unit: "{orders}",
            description: "Total number of orders processed by the background worker");
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var intervalSeconds = _options.Value.IntervalSeconds > 0
            ? _options.Value.IntervalSeconds
            : 30;

        _logger.LogInformation(
            "OrderProcessingBackgroundService started with an interval of {IntervalSeconds} seconds.",
            intervalSeconds);

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(intervalSeconds));

        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            await ProcessCycleAsync(stoppingToken);
        }

        _logger.LogInformation("OrderProcessingBackgroundService is stopping gracefully.");
    }

    private async Task ProcessCycleAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Background cycle triggered: Starting pending order processing and dashboard refresh.");
        _workerCyclesCounter.Add(1);

        try
        {
            // Create an explicit dependency injection scope for scoped services
            using var scope = _scopeFactory.CreateScope();

            var orderProcessingService = scope.ServiceProvider.GetRequiredService<IOrderProcessingService>();
            var dashboardRefreshService = scope.ServiceProvider.GetRequiredService<IOrderDashboardRefreshService>();

            // Step 1: Process pending orders (and invalidate their Redis cache entries)
            var processedOrderIds = await orderProcessingService.ProcessPendingOrdersAsync(cancellationToken);

            if (processedOrderIds.Count > 0)
            {
                _workerProcessedCounter.Add(processedOrderIds.Count);
                _logger.LogInformation("Background worker processed {Count} pending order(s): {OrderIds}",
                    processedOrderIds.Count, string.Join(", ", processedOrderIds));
            }

            // Step 2: Refresh the Materialized View read model
            await dashboardRefreshService.RefreshDashboardAsync(cancellationToken);

            _logger.LogInformation(
                "Background cycle completed successfully. Processed {Count} order(s).",
                processedOrderIds.Count);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            _logger.LogInformation("Background cycle cancelled due to application shutdown.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "An unhandled error occurred during background cycle execution.");
        }
    }
}
