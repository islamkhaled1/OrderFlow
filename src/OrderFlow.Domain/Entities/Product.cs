namespace OrderFlow.Domain.Entities;

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }

    /// <summary>
    /// Units available for new orders. Reservation (decrement) and release (increment) are performed
    /// by atomic conditional SQL updates, never by read-modify-write in memory.
    /// </summary>
    public int StockQuantity { get; set; }

    /// <summary>Inactive products cannot be ordered.</summary>
    public bool IsActive { get; set; } = true;

    // For EF Core
    private Product() { }

    public Product(int id, string name, decimal price, int stockQuantity = 0)
        : this(name, price, stockQuantity)
    {
        Id = id;
    }

    public Product(string name, decimal price, int stockQuantity = 0)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Product name cannot be empty.", nameof(name));
        if (price < 0)
            throw new ArgumentException("Product price cannot be negative.", nameof(price));
        if (stockQuantity < 0)
            throw new ArgumentException("Stock quantity cannot be negative.", nameof(stockQuantity));

        Name = name.Trim();
        Price = price;
        StockQuantity = stockQuantity;
    }
}
