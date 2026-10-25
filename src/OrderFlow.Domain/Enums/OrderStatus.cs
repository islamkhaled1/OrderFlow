namespace OrderFlow.Domain.Enums;

/// <summary>
/// Order lifecycle:
///   Pending   -> Completed   (fulfilled by an administrator)
///   Pending   -> Cancelled   (by the owner/admin, or automatically when it expires)
/// Completed and Cancelled are terminal states.
/// Stock is reserved when an order is created (Pending) and released only on Pending -> Cancelled.
/// </summary>
public enum OrderStatus
{
    Pending = 1,
    Completed = 2,
    Cancelled = 3
}
