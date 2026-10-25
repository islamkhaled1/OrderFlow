using OrderFlow.Domain.Exceptions;

namespace OrderFlow.Domain.Entities;

public class OrderItem
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order? Order { get; set; }

    /// <summary>
    /// Product this line reserved stock from. Null only for legacy rows created before inventory
    /// tracking existed; those never reserved stock and therefore never release any.
    /// </summary>
    public int? ProductId { get; private set; }
    public string ProductName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal UnitPrice { get; set; }

    public decimal LineTotal => Quantity * UnitPrice;

    // For EF Core
    private OrderItem() { }

    public OrderItem(string productName, int quantity, decimal unitPrice)
        : this(null, productName, quantity, unitPrice)
    {
    }

    public OrderItem(int? productId, string productName, int quantity, decimal unitPrice)
    {
        if (string.IsNullOrWhiteSpace(productName))
            throw new DomainException("Product name snapshot cannot be empty.");

        if (quantity <= 0)
            throw new DomainException("Order item quantity must be greater than zero.");

        if (unitPrice < 0)
            throw new DomainException("Order item unit price cannot be negative.");

        ProductId = productId;
        ProductName = productName.Trim();
        Quantity = quantity;
        UnitPrice = unitPrice;
    }
}
