using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OrderFlow.Domain.Entities;
using OrderFlow.Domain.Enums;
using OrderFlow.Infrastructure.ReadModels;

namespace OrderFlow.Infrastructure.Persistence.Seed;

public static class OrderFlowDbContextSeed
{
    public static async Task SeedAsync(OrderFlowDbContext context, ILogger logger)
    {
        try
        {
            // Seed Customers
            if (!await context.Customers.AnyAsync())
            {
                logger.LogInformation("Seeding initial customers...");
                var customers = new List<Customer>
                {
                    new("John Doe"),
                    new("Jane Smith"),
                    new("Alice Johnson")
                };

                await context.Customers.AddRangeAsync(customers);
                await context.SaveChangesAsync();
                logger.LogInformation("Customers seeded successfully.");
            }

            // Seed Products
            if (!await context.Products.AnyAsync())
            {
                logger.LogInformation("Seeding initial products...");
                var products = new List<Product>
                {
                    new("Mechanical Keyboard", 120.00m),
                    new("Wireless Mouse", 60.00m),
                    new("4K Ultra HD Monitor", 450.00m),
                    new("USB-C Multiport Adapter", 40.00m),
                    new("Noise-Cancelling Headphones", 200.00m)
                };

                await context.Products.AddRangeAsync(products);
                await context.SaveChangesAsync();
                logger.LogInformation("Products seeded successfully.");
            }

            // Optional sample initial order
            if (!await context.Orders.AnyAsync())
            {
                logger.LogInformation("Seeding initial sample order...");
                var customer = await context.Customers.FirstAsync();
                var products = await context.Products.Take(2).ToListAsync();

                var items = new List<OrderItem>
                {
                    new(products[0].Name, 2, products[0].Price),
                    new(products[1].Name, 1, products[1].Price)
                };

                var order = Order.Create(customer.Id, items);
                await context.Orders.AddAsync(order);
                await context.SaveChangesAsync();

                // Also populate initial read model entry
                var readModel = new OrderDashboardReadModel
                {
                    OrderId = order.Id,
                    CustomerName = customer.Name,
                    ItemCount = order.Items.Count,
                    Total = order.Total,
                    Status = order.Status.ToString(),
                    CreatedAt = order.CreatedAt
                };

                await context.OrderDashboardReadModels.AddAsync(readModel);
                await context.SaveChangesAsync();
                logger.LogInformation("Sample order seeded successfully.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding the database.");
            throw;
        }
    }
}
