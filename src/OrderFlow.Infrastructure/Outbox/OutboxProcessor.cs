using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderFlow.Application.Interfaces;
using OrderFlow.Infrastructure.Observability;
using OrderFlow.Infrastructure.Persistence;
using OrderFlow.Infrastructure.Persistence.Entities;

namespace OrderFlow.Infrastructure.Outbox;

public class OutboxOptions
{
    public const string SectionName = "Outbox";

    public int PollIntervalSeconds { get; set; } = 2;
    public int BatchSize { get; set; } = 20;

    /// <summary>After this many failed attempts a message is dead-lettered.</summary>
    public int MaxAttempts { get; set; } = 8;

    /// <summary>A claim lasts this long; if a worker dies the message becomes claimable again afterwards.</summary>
    public int LeaseSeconds { get; set; } = 120;

    public int BaseBackoffSeconds { get; set; } = 5;
    public int MaxBackoffSeconds { get; set; } = 300;

    /// <summary>Processed messages are kept this long for diagnostics, then purged.</summary>
    public int ProcessedRetentionDays { get; set; } = 7;
}

/// <summary>
/// Claims and processes outbox messages. Delivery is AT-LEAST-ONCE: a crash after the side effects but
/// before the message is marked processed (or an expired lease) causes redelivery, so every handler step
/// here is idempotent (read-model upsert recomputes from current state; cache removal is a delete).
/// </summary>
public class OutboxProcessor
{
    private readonly OrderFlowDbContext _db;
    private readonly IOrderDashboardRefreshService _dashboard;
    private readonly ICacheService _cache;
    private readonly IOptions<OutboxOptions> _options;
    private readonly InfrastructureMetrics _metrics;
    private readonly ILogger<OutboxProcessor> _logger;
    private readonly TimeProvider _time;

    public OutboxProcessor(
        OrderFlowDbContext db,
        IOrderDashboardRefreshService dashboard,
        ICacheService cache,
        IOptions<OutboxOptions> options,
        InfrastructureMetrics metrics,
        ILogger<OutboxProcessor> logger,
        TimeProvider? time = null)
    {
        _db = db;
        _dashboard = dashboard;
        _cache = cache;
        _options = options;
        _metrics = metrics;
        _logger = logger;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>Processes one batch. Returns the number of messages handled successfully.</summary>
    public async Task<int> ProcessBatchAsync(CancellationToken cancellationToken)
    {
        var opts = _options.Value;
        var claimToken = Guid.NewGuid();
        var now = _time.GetUtcNow().UtcDateTime;
        var leaseUntil = now.AddSeconds(opts.LeaseSeconds);

        // Atomic claim: READPAST skips rows locked by other workers, UPDLOCK makes the claim exclusive,
        // so concurrent workers never pick the same message.
        await _db.Database.ExecuteSqlInterpolatedAsync($"""
            WITH batch AS (
                SELECT TOP ({opts.BatchSize}) *
                FROM OutboxMessages WITH (ROWLOCK, UPDLOCK, READPAST)
                WHERE ProcessedAtUtc IS NULL
                  AND DeadLetteredAtUtc IS NULL
                  AND NextAttemptAtUtc <= {now}
                  AND (LockedUntilUtc IS NULL OR LockedUntilUtc < {now})
                ORDER BY NextAttemptAtUtc, OccurredAtUtc)
            UPDATE batch SET LockedBy = {claimToken}, LockedUntilUtc = {leaseUntil}
            """, cancellationToken);

        var claimed = await _db.OutboxMessages
            .AsNoTracking()
            .Where(m => m.LockedBy == claimToken && m.ProcessedAtUtc == null)
            .OrderBy(m => m.OccurredAtUtc)
            .ToListAsync(cancellationToken);

        var succeeded = 0;
        foreach (var message in claimed)
        {
            cancellationToken.ThrowIfCancellationRequested();

            using var activity = InfrastructureTelemetry.ActivitySource.StartActivity("outbox.process");
            activity?.SetTag("outbox.event_type", message.EventType);
            activity?.SetTag("outbox.message_id", message.Id.ToString());
            activity?.SetTag("outbox.attempt", message.Attempts + 1);

            // Each message starts with a clean tracker so a failure cannot poison the next one.
            _db.ChangeTracker.Clear();

            try
            {
                await HandleAsync(message, cancellationToken);
                await MarkProcessedAsync(message.Id, cancellationToken);
                _metrics.OutboxProcessed(message.EventType);
                succeeded++;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw; // shutting down: the lease expires and the message is redelivered
            }
            catch (Exception ex)
            {
                activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error, ex.Message);
                await RecordFailureAsync(message, ex, cancellationToken);
            }
        }

        return succeeded;
    }

    private async Task HandleAsync(OutboxMessage message, CancellationToken cancellationToken)
    {
        switch (message.EventType)
        {
            case OutboxEventTypes.OrderCreated:
            case OutboxEventTypes.OrderCompleted:
            case OutboxEventTypes.OrderCancelled:
                var payload = JsonSerializer.Deserialize<OrderEventPayload>(message.Payload, JsonSerializerOptions.Web)
                    ?? throw new InvalidOperationException("Outbox payload is empty.");

                // 1. Incremental read-model update, recomputed from current transactional state.
                await _dashboard.UpsertOrderAsync(payload.OrderId, cancellationToken);

                // 2. Drop the cached order details. A false result means Redis was unreachable: fail the
                //    attempt so it is retried with backoff instead of leaving a stale entry silently.
                if (!await _cache.RemoveAsync($"order:{payload.OrderId}", cancellationToken))
                    throw new InvalidOperationException($"Cache invalidation for order {payload.OrderId} failed (cache unavailable).");
                break;

            default:
                throw new InvalidOperationException($"Unknown outbox event type '{message.EventType}'.");
        }
    }

    private Task MarkProcessedAsync(Guid id, CancellationToken cancellationToken)
    {
        var now = _time.GetUtcNow().UtcDateTime;
        return _db.OutboxMessages.Where(m => m.Id == id).ExecuteUpdateAsync(s => s
            .SetProperty(m => m.ProcessedAtUtc, now)
            .SetProperty(m => m.LockedBy, (Guid?)null)
            .SetProperty(m => m.LockedUntilUtc, (DateTime?)null)
            .SetProperty(m => m.LastError, (string?)null), cancellationToken);
    }

    private async Task RecordFailureAsync(OutboxMessage message, Exception ex, CancellationToken cancellationToken)
    {
        var opts = _options.Value;
        var attempts = message.Attempts + 1;
        var now = _time.GetUtcNow().UtcDateTime;
        var error = ex.GetType().Name + ": " + ex.Message;
        if (error.Length > 2000) error = error[..2000];

        _db.ChangeTracker.Clear();

        if (attempts >= opts.MaxAttempts)
        {
            _logger.LogError(ex,
                "Outbox message {MessageId} ({EventType}) dead-lettered after {Attempts} attempts.",
                message.Id, message.EventType, attempts);

            await _db.OutboxMessages.Where(m => m.Id == message.Id).ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Attempts, attempts)
                .SetProperty(m => m.LastError, error)
                .SetProperty(m => m.DeadLetteredAtUtc, now)
                .SetProperty(m => m.LockedBy, (Guid?)null)
                .SetProperty(m => m.LockedUntilUtc, (DateTime?)null), cancellationToken);

            _metrics.OutboxDeadLettered(message.EventType);
            return;
        }

        var delay = ComputeBackoff(attempts, opts);
        _logger.LogWarning(ex,
            "Outbox message {MessageId} ({EventType}) failed (attempt {Attempts}/{Max}); retrying in {Delay}.",
            message.Id, message.EventType, attempts, opts.MaxAttempts, delay);

        await _db.OutboxMessages.Where(m => m.Id == message.Id).ExecuteUpdateAsync(s => s
            .SetProperty(m => m.Attempts, attempts)
            .SetProperty(m => m.LastError, error)
            .SetProperty(m => m.NextAttemptAtUtc, now.Add(delay))
            .SetProperty(m => m.LockedBy, (Guid?)null)
            .SetProperty(m => m.LockedUntilUtc, (DateTime?)null), cancellationToken);

        _metrics.OutboxFailed(message.EventType);
    }

    /// <summary>Exponential backoff (base * 2^(attempt-1)) capped at the configured maximum.</summary>
    public static TimeSpan ComputeBackoff(int attempt, OutboxOptions options)
    {
        var exponent = Math.Min(Math.Max(attempt - 1, 0), 20);
        var seconds = Math.Min((double)options.BaseBackoffSeconds * Math.Pow(2, exponent), options.MaxBackoffSeconds);
        return TimeSpan.FromSeconds(seconds);
    }
}
