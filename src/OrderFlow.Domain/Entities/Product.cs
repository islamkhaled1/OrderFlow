namespace OrderFlow.Domain.Entities;

public class Product
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }

    // For EF Core
    private Product() { }

    public Product(int id, string name, decimal price)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Product name cannot be empty.", nameof(name));
        if (price < 0)
            throw new ArgumentException("Product price cannot be negative.", nameof(price));

        Id = id;
        Name = name.Trim();
        Price = price;
    }

    public Product(string name, decimal price)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Product name cannot be empty.", nameof(name));
        if (price < 0)
            throw new ArgumentException("Product price cannot be negative.", nameof(price));

        Name = name.Trim();
        Price = price;
    }
}
