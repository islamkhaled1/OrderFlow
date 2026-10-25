using OrderFlow.Domain.Enums;
using OrderFlow.Domain.Exceptions;

namespace OrderFlow.Domain.Entities;

public class Order
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public OrderStatus Status { get; private set; }
    public DateTime CreatedAt { get; set; }
    public decimal Total { get; private set; }
    public DateTime? CompletedAt { get; private set; }
    public DateTime? CancelledAt { get; private set; }
    public string? CancellationReason { get; private set; }

    /// <summary>SQL Server rowversion used for optimistic concurrency on state transitions.</summary>
    public byte[] RowVersion { get; private set; } = Array.Empty<byte>();

    private readonly List<OrderItem> _items = new();
    public IReadOnlyCollection<OrderItem> Items => _items.AsReadOnly();

    // For EF Core
    private Order() { }

    public static Order Create(int customerId, IEnumerable<OrderItem> items)
    {
        if (customerId <= 0)
            throw new DomainException("Customer ID must be greater than zero.");

        var itemList = items?.ToList();
        if (itemList == null || itemList.Count == 0)
            throw new DomainException("An order must contain at least one order item.");

        var order = new Order
        {
            CustomerId = customerId,
            Status = OrderStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        foreach (var item in itemList)
        {
            order._items.Add(item);
        }

        order.CalculateTotal();
        return order;
    }

    public static bool CanTransition(OrderStatus from, OrderStatus to) =>
        from == OrderStatus.Pending && (to == OrderStatus.Completed || to == OrderStatus.Cancelled);

    public void MarkAsCompleted()
    {
        if (!CanTransition(Status, OrderStatus.Completed))
            throw new InvalidOrderStateTransitionException(Id, Status, OrderStatus.Completed);

        Status = OrderStatus.Completed;
        CompletedAt = DateTime.UtcNow;
    }

    /// <summary>
    /// Cancels a pending order. The caller is responsible for releasing the reserved stock in the same
    /// transaction; this method is the single guard that makes the release happen at most once.
    /// </summary>
    public void Cancel(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason))
            throw new DomainException("A cancellation reason is required.");

        if (!CanTransition(Status, OrderStatus.Cancelled))
            throw new InvalidOrderStateTransitionException(Id, Status, OrderStatus.Cancelled);

        Status = OrderStatus.Cancelled;
        CancelledAt = DateTime.UtcNow;
        CancellationReason = reason.Trim();
    }

    private void CalculateTotal()
    {
        Total = _items.Sum(item => item.LineTotal);
    }
}
