using OrderFlow.Domain.Enums;
using OrderFlow.Domain.Exceptions;

namespace OrderFlow.Domain.Entities;

public class Order
{
    public int Id { get; set; }
    public int CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public OrderStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public decimal Total { get; set; }

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

    public void MarkAsCompleted()
    {
        if (Status != OrderStatus.Pending)
        {
            throw new DomainException($"Order {Id} is already '{Status}' and cannot be transitioned to 'Completed'.");
        }

        Status = OrderStatus.Completed;
    }

    private void CalculateTotal()
    {
        Total = _items.Sum(item => item.LineTotal);
    }
}
