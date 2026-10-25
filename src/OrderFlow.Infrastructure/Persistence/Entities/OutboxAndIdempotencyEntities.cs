namespace OrderFlow.Infrastructure.Persistence.Entities;

/// <summary>
/// Transactional outbox row. Written in the same transaction as the business change; delivered
/// at-least-once by <c>OutboxProcessor</c>. Consumers must be idempotent.
/// </summary>
public class OutboxMessage
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string EventType { get; set; } = string.Empty;
    public string Payload { get; set; } = string.Empty;
    public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? ProcessedAtUtc { get; set; }

    /// <summary>Number of failed processing attempts so far.</summary>
    public int Attempts { get; set; }

    /// <summary>Earliest time the message may be (re)claimed; implements bounded exponential backoff.</summary>
    public DateTime NextAttemptAtUtc { get; set; } = DateTime.UtcNow;

    /// <summary>Claim lease: another worker may re-claim the message only after this time (crash recovery).</summary>
    public DateTime? LockedUntilUtc { get; set; }
    public Guid? LockedBy { get; set; }

    public string? LastError { get; set; }

    /// <summary>Set when attempts are exhausted; the message is parked for investigation, not retried.</summary>
    public DateTime? DeadLetteredAtUtc { get; set; }
}

/// <summary>Durable record of an Idempotency-Key, scoped to the authenticated caller.</summary>
public class IdempotencyRecord
{
    public long Id { get; set; }
    public string Scope { get; set; } = string.Empty;
    public string Key { get; set; } = string.Empty;
    public string RequestHash { get; set; } = string.Empty;
    public int? StatusCode { get; set; }
    public string? ResponseBody { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}
