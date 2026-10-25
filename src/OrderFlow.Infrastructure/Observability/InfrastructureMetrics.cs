using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OrderFlow.Domain.Enums;

namespace OrderFlow.Infrastructure.Observability;

/// <summary>Shared tracing source for background processing (added to OpenTelemetry in the API).</summary>
public static class InfrastructureTelemetry
{
    public const string ActivitySourceName = "OrderFlow.Background";
    public static readonly ActivitySource ActivitySource = new(ActivitySourceName, "1.0.0");
}

/// <summary>
/// Counters emitted by infrastructure components (cache, outbox, workers). They share the "OrderFlow"
/// meter with the API metrics so a single Prometheus scrape exposes everything. Labels are low-cardinality
/// (event type / operation) and never contain customer data.
/// </summary>
public sealed class InfrastructureMetrics
{
    public const string MeterName = "OrderFlow";

    private readonly Counter<long> _cacheHits;
    private readonly Counter<long> _cacheMisses;
    private readonly Counter<long> _cacheErrors;
    private readonly Counter<long> _outboxProcessed;
    private readonly Counter<long> _outboxFailed;
    private readonly Counter<long> _outboxDeadLettered;
    private readonly Counter<long> _workerFailures;
    private readonly Counter<long> _ordersExpired;

    public InfrastructureMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MeterName);
        _cacheHits = meter.CreateCounter<long>("orderflow.cache.hits", "{hits}", "Cache hits");
        _cacheMisses = meter.CreateCounter<long>("orderflow.cache.misses", "{misses}", "Cache misses");
        _cacheErrors = meter.CreateCounter<long>("orderflow.cache.errors", "{errors}", "Expected cache failures (Redis unavailable/timeout)");
        _outboxProcessed = meter.CreateCounter<long>("orderflow.outbox.processed", "{messages}", "Outbox messages processed successfully");
        _outboxFailed = meter.CreateCounter<long>("orderflow.outbox.failed", "{attempts}", "Failed outbox processing attempts (will be retried)");
        _outboxDeadLettered = meter.CreateCounter<long>("orderflow.outbox.dead_lettered", "{messages}", "Outbox messages moved to dead-letter");
        _workerFailures = meter.CreateCounter<long>("orderflow.worker.failures", "{failures}", "Unhandled background worker cycle failures");
        _ordersExpired = meter.CreateCounter<long>("orderflow.orders.expired", "{orders}", "Pending orders cancelled by the expiry job");
    }

    public void CacheHit() => _cacheHits.Add(1);
    public void CacheMiss() => _cacheMisses.Add(1);
    public void CacheError(string operation) => _cacheErrors.Add(1, new KeyValuePair<string, object?>("operation", operation));
    public void OutboxProcessed(string eventType) => _outboxProcessed.Add(1, new KeyValuePair<string, object?>("event_type", eventType));
    public void OutboxFailed(string eventType) => _outboxFailed.Add(1, new KeyValuePair<string, object?>("event_type", eventType));
    public void OutboxDeadLettered(string eventType) => _outboxDeadLettered.Add(1, new KeyValuePair<string, object?>("event_type", eventType));
    public void WorkerFailure(string worker) => _workerFailures.Add(1, new KeyValuePair<string, object?>("worker", worker));
    public void OrdersExpired(int count) => _ordersExpired.Add(count);
}

/// <summary>Observable gauges for the outbox backlog and dead letters (queried on scrape).</summary>
public sealed class OutboxBacklogMetrics
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<OutboxBacklogMetrics> _logger;

    public OutboxBacklogMetrics(IMeterFactory meterFactory, IServiceScopeFactory scopeFactory, ILogger<OutboxBacklogMetrics> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;

        var meter = meterFactory.Create(InfrastructureMetrics.MeterName);
        meter.CreateObservableGauge("orderflow.outbox.pending", () => Count(pending: true), "{messages}",
            "Outbox messages waiting to be processed");
        meter.CreateObservableGauge("orderflow.outbox.dead_letter_backlog", () => Count(pending: false), "{messages}",
            "Outbox messages currently dead-lettered and awaiting investigation");
    }

    private long Count(bool pending)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<Persistence.OrderFlowDbContext>();
            return pending
                ? db.OutboxMessages.AsNoTracking().Count(m => m.ProcessedAtUtc == null && m.DeadLetteredAtUtc == null)
                : db.OutboxMessages.AsNoTracking().Count(m => m.DeadLetteredAtUtc != null);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to query outbox backlog for metrics.");
            return -1;
        }
    }
}
