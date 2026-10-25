using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderFlow.Infrastructure.Outbox;
using OrderFlow.Infrastructure.Persistence;

namespace OrderFlow.Infrastructure.Services;

/// <summary>
/// Retention policies:
///  - Processed outbox messages: purged after <see cref="OutboxOptions.ProcessedRetentionDays"/> (default 7 days).
///    Dead-lettered messages are never purged automatically; they wait for an operator.
///  - Idempotency records: purged once ExpiresAtUtc passes (default 24 h after creation).
/// Deletes run in bounded batches to keep locks and log growth small.
/// </summary>
public class RetentionService
{
    private const int DeleteBatchSize = 1000;

    private readonly OrderFlowDbContext _context;
    private readonly IOptions<OutboxOptions> _outboxOptions;
    private readonly ILogger<RetentionService> _logger;

    public RetentionService(OrderFlowDbContext context, IOptions<OutboxOptions> outboxOptions, ILogger<RetentionService> logger)
    {
        _context = context;
        _outboxOptions = outboxOptions;
        _logger = logger;
    }

    public async Task<int> PurgeProcessedOutboxAsync(CancellationToken cancellationToken)
    {
        var cutoff = DateTime.UtcNow.AddDays(-Math.Max(1, _outboxOptions.Value.ProcessedRetentionDays));
        var total = 0;
        int deleted;
        do
        {
            deleted = await _context.OutboxMessages
                .Where(m => m.ProcessedAtUtc != null && m.ProcessedAtUtc < cutoff)
                .OrderBy(m => m.ProcessedAtUtc)
                .Take(DeleteBatchSize)
                .ExecuteDeleteAsync(cancellationToken);
            total += deleted;
        } while (deleted == DeleteBatchSize && !cancellationToken.IsCancellationRequested);

        if (total > 0) _logger.LogInformation("Purged {Count} processed outbox message(s).", total);
        return total;
    }

    public async Task<int> PurgeExpiredIdempotencyRecordsAsync(CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var total = 0;
        int deleted;
        do
        {
            deleted = await _context.IdempotencyRecords
                .Where(r => r.ExpiresAtUtc < now)
                .OrderBy(r => r.ExpiresAtUtc)
                .Take(DeleteBatchSize)
                .ExecuteDeleteAsync(cancellationToken);
            total += deleted;
        } while (deleted == DeleteBatchSize && !cancellationToken.IsCancellationRequested);

        if (total > 0) _logger.LogInformation("Purged {Count} expired idempotency record(s).", total);
        return total;
    }
}
