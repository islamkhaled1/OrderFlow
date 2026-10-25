using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OrderFlow.Application.Interfaces;
using OrderFlow.Domain.Entities;

namespace OrderFlow.Infrastructure.Persistence.Seed;

public static class OrderFlowDbContextSeed
{
    /// <summary>Seeds demo products (with stock). Orders are never seeded: they must go through the real
    /// creation path so stock, outbox and read model stay consistent.</summary>
    public static async Task SeedAsync(OrderFlowDbContext context, ILogger logger)
    {
        try
        {
            if (!await context.Customers.AnyAsync())
            {
                logger.LogInformation("Seeding initial customers...");
                await context.Customers.AddRangeAsync(
                    new Customer("John Doe"),
                    new Customer("Jane Smith"),
                    new Customer("Alice Johnson"));
                await context.SaveChangesAsync();
            }

            if (!await context.Products.AnyAsync())
            {
                logger.LogInformation("Seeding initial products...");
                await context.Products.AddRangeAsync(
                    new Product("Mechanical Keyboard", 120.00m, 50),
                    new Product("Wireless Mouse", 60.00m, 100),
                    new Product("4K Ultra HD Monitor", 450.00m, 20),
                    new Product("USB-C Multiport Adapter", 40.00m, 200),
                    new Product("Noise-Cancelling Headphones", 200.00m, 30));
                await context.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while seeding the database.");
            throw;
        }
    }

    /// <summary>
    /// Creates the administrator account if credentials are supplied through configuration/environment
    /// (Seed:AdminEmail / Seed:AdminPassword). Nothing is created when they are absent, and no default
    /// credentials exist in the repository.
    /// </summary>
    public static async Task SeedAdminAsync(
        OrderFlowDbContext context,
        IPasswordHasherService hasher,
        string? email,
        string? password,
        ILogger logger)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            logger.LogInformation("No Seed:AdminEmail/Seed:AdminPassword configured; skipping admin seeding.");
            return;
        }

        var normalized = AppUser.NormalizeEmail(email);
        if (await context.Users.AnyAsync(u => u.Email == normalized))
            return;

        context.Users.Add(AppUser.CreateAdminUser(normalized, hasher.Hash(password)));
        await context.SaveChangesAsync();
        logger.LogInformation("Administrator account seeded.");
    }
}
