using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using OrderFlow.Application.Interfaces;
using OrderFlow.Infrastructure.Persistence;

namespace OrderFlow.Infrastructure.Idempotency;

public class IdempotencyOptions
{
    public const string SectionName = "Idempotency";

    /// <summary>How long a key (and its replayable response) is retained. Expired rows are purged by the maintenance job.</summary>
    public int RetentionHours { get; set; } = 24;
}

/// <summary>
/// Durable idempotency in SQL Server. A claim is a conditional INSERT guarded by UPDLOCK/HOLDLOCK plus a
/// unique index on (Scope, Key): of N concurrent requests with the same key exactly one inserts, the others
/// wait on the lock until that transaction finishes and then see its outcome.
/// </summary>
public class IdempotencyStore : IIdempotencyStore
{
    private readonly OrderFlowDbContext _context;
    private readonly IOptions<IdempotencyOptions> _options;

    public IdempotencyStore(OrderFlowDbContext context, IOptions<IdempotencyOptions> options)
    {
        _context = context;
        _options = options;
    }

    public async Task<bool> TryClaimAsync(string scope, string key, string requestHash, CancellationToken cancellationToken)
    {
        var now = DateTime.UtcNow;
        var expires = now.AddHours(_options.Value.RetentionHours > 0 ? _options.Value.RetentionHours : 24);

        // An expired-but-not-yet-purged record must not block a fresh request.
        await _context.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM IdempotencyRecords WHERE Scope = {scope} AND [Key] = {key} AND ExpiresAtUtc < {now}",
            cancellationToken);

        var inserted = await _context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO IdempotencyRecords (Scope, [Key], RequestHash, CreatedAtUtc, ExpiresAtUtc)
            SELECT {scope}, {key}, {requestHash}, {now}, {expires}
            WHERE NOT EXISTS (
                SELECT 1 FROM IdempotencyRecords WITH (UPDLOCK, HOLDLOCK)
                WHERE Scope = {scope} AND [Key] = {key})
            """, cancellationToken);

        return inserted == 1;
    }

    public async Task<IdempotencyEntry?> GetAsync(string scope, string key, CancellationToken cancellationToken)
    {
        return await _context.IdempotencyRecords
            .AsNoTracking()
            .Where(r => r.Scope == scope && r.Key == key)
            .Select(r => new IdempotencyEntry(r.RequestHash, r.StatusCode, r.ResponseBody))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task CompleteAsync(string scope, string key, int statusCode, string responseBody, CancellationToken cancellationToken)
    {
        await _context.IdempotencyRecords
            .Where(r => r.Scope == scope && r.Key == key)
            .ExecuteUpdateAsync(s => s
                .SetProperty(r => r.StatusCode, statusCode)
                .SetProperty(r => r.ResponseBody, responseBody), cancellationToken);
    }
}
