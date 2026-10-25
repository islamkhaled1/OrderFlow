using OrderFlow.Domain.Entities;

namespace OrderFlow.Application.Interfaces;

public interface ICurrentUser
{
    bool IsAuthenticated { get; }
    Guid? UserId { get; }
    int? CustomerId { get; }
    bool IsAdmin { get; }
}

public interface IUnitOfWork
{
    /// <summary>
    /// Runs <paramref name="operation"/> in one database transaction using the provider's execution
    /// strategy (so SQL retry-on-failure and explicit transactions compose). The operation may be
    /// re-executed on transient failures, so it must load everything it needs inside the delegate.
    /// Optimistic-concurrency conflicts are surfaced as <c>ConcurrencyConflictException</c>.
    /// </summary>
    Task<T> ExecuteInTransactionAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken cancellationToken);

    /// <summary>Saves pending changes; unique-key violations are surfaced as <c>ConflictException</c>.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);
}

public interface IUserRepository
{
    Task<AppUser?> GetByEmailAsync(string normalizedEmail, CancellationToken cancellationToken);
    Task<bool> EmailExistsAsync(string normalizedEmail, CancellationToken cancellationToken);
    void Add(AppUser user);
}

public interface IPasswordHasherService
{
    string Hash(string password);
    bool Verify(string passwordHash, string password);
}

public sealed record AuthToken(string AccessToken, DateTime ExpiresAtUtc);

public interface ITokenService
{
    AuthToken Create(AppUser user);
}

/// <summary>Transactional outbox: messages are written in the same transaction as the business change.</summary>
public interface IOutbox
{
    void Enqueue(string eventType, object payload);
}

public static class OutboxEventTypes
{
    public const string OrderCreated = "OrderCreated";
    public const string OrderCompleted = "OrderCompleted";
    public const string OrderCancelled = "OrderCancelled";
}

public sealed record OrderEventPayload(int OrderId, int CustomerId);

public sealed record IdempotencyEntry(string RequestHash, int? StatusCode, string? ResponseBody);

public interface IIdempotencyStore
{
    /// <summary>
    /// Atomically claims (scope, key). Returns false if it already exists. Must be called inside the
    /// same transaction as the business change so a rolled-back operation leaves no claim behind.
    /// </summary>
    Task<bool> TryClaimAsync(string scope, string key, string requestHash, CancellationToken cancellationToken);
    Task<IdempotencyEntry?> GetAsync(string scope, string key, CancellationToken cancellationToken);
    Task CompleteAsync(string scope, string key, int statusCode, string responseBody, CancellationToken cancellationToken);
}

public interface IOrderExpirationService
{
    /// <summary>Cancels pending orders older than the cutoff (releasing their stock). Returns how many were cancelled.</summary>
    Task<int> ExpireStaleOrdersAsync(DateTime createdBeforeUtc, int batchSize, CancellationToken cancellationToken);
}
