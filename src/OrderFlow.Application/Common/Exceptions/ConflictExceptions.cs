namespace OrderFlow.Application.Common.Exceptions;

/// <summary>The request is valid but conflicts with current state (maps to 409).</summary>
public class ConflictException : Exception
{
    public ConflictException(string message) : base(message) { }
}

public sealed record StockShortage(int ProductId, int Requested);

/// <summary>One or more products are inactive or do not have enough stock.</summary>
public class InsufficientStockException : ConflictException
{
    public IReadOnlyList<StockShortage> Shortages { get; }

    public InsufficientStockException(IReadOnlyList<StockShortage> shortages)
        : base("Insufficient stock for product(s): " + string.Join(", ", shortages.Select(s => s.ProductId)))
    {
        Shortages = shortages;
    }
}

/// <summary>An Idempotency-Key was reused with a different request payload (maps to 422).</summary>
public class IdempotencyKeyReuseException : Exception
{
    public IdempotencyKeyReuseException()
        : base("This Idempotency-Key was already used with a different request payload.") { }
}

/// <summary>Authenticated, but not allowed (maps to 403).</summary>
public class ForbiddenException : Exception
{
    public ForbiddenException(string message) : base(message) { }
}

/// <summary>Authentication failed (maps to 401).</summary>
public class UnauthorizedException : Exception
{
    public UnauthorizedException(string message) : base(message) { }
}

/// <summary>Optimistic concurrency conflict: someone else changed the same row first (maps to 409).</summary>
public class ConcurrencyConflictException : ConflictException
{
    public ConcurrencyConflictException()
        : base("The resource was modified by another request. Please retry.") { }
}
