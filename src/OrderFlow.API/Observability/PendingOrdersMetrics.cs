using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Domain.Enums;
using OrderFlow.Infrastructure.Persistence;

namespace OrderFlow.API.Observability;

/// <summary>
/// Provides an observable gauge for pending orders count.
/// Uses IServiceScopeFactory to safely access the scoped DbContext from a singleton meter callback.
/// </summary>
public sealed class PendingOrdersMetrics : IDisposable
{
    private readonly ObservableGauge<long> _pendingOrdersGauge;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PendingOrdersMetrics> _logger;

    public PendingOrdersMetrics(
        IMeterFactory meterFactory,
        IServiceScopeFactory scopeFactory,
        ILogger<PendingOrdersMetrics> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        var meter = meterFactory.Create(OrderFlowMetrics.MeterName);
        _pendingOrdersGauge = meter.CreateObservableGauge<long>(
            "orderflow.orders.pending",
            observeValue: GetPendingOrdersCount,
            unit: "{orders}",
            description: "Current number of pending orders");
    }

    private long GetPendingOrdersCount()
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<OrderFlowDbContext>();
            return dbContext.Orders
                .AsNoTracking()
                .Count(o => o.Status == OrderStatus.Pending);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query pending orders count for metrics.");
            return -1;
        }
    }

    public void Dispose()
    {
        // No-op, meter is managed by DI
    }
}
