using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OrderFlow.Application.Interfaces;
using OrderFlow.Domain.Entities;
using OrderFlow.Infrastructure.BackgroundServices;
using OrderFlow.Infrastructure.Persistence;
using OrderFlow.Infrastructure.ReadModels;
using OrderFlow.IntegrationTests.Infrastructure;

namespace OrderFlow.IntegrationTests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _databaseName = "OrderFlowIntegrationTestDb_" + Guid.NewGuid();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            // 1. Remove background service to keep integration tests deterministic
            var backgroundServiceDescriptor = services.SingleOrDefault(
                d => d.ImplementationType == typeof(OrderProcessingBackgroundService));
            if (backgroundServiceDescriptor != null)
            {
                services.Remove(backgroundServiceDescriptor);
            }

            // 2. Remove all existing EF Core descriptors (to avoid provider conflict with SqlServer)
            var efDescriptors = services
                .Where(d => d.ServiceType.Namespace?.StartsWith("Microsoft.EntityFrameworkCore") == true ||
                            d.ImplementationType?.Namespace?.StartsWith("Microsoft.EntityFrameworkCore") == true ||
                            d.ServiceType == typeof(OrderFlowDbContext) ||
                            d.ServiceType == typeof(DbContextOptions) ||
                            d.ServiceType == typeof(DbContextOptions<OrderFlowDbContext>))
                .ToList();

            foreach (var descriptor in efDescriptors)
            {
                services.Remove(descriptor);
            }

            // 3. Register isolated in-memory database provider
            var inMemoryServiceProvider = new ServiceCollection()
                .AddEntityFrameworkInMemoryDatabase()
                .BuildServiceProvider();

            services.AddDbContext<OrderFlowDbContext>(options =>
            {
                options.UseInMemoryDatabase(_databaseName)
                       .UseInternalServiceProvider(inMemoryServiceProvider);
            });

            // 4. Replace Redis Cache Service with In-Memory Test Cache
            var cacheDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(ICacheService));
            if (cacheDescriptor != null)
            {
                services.Remove(cacheDescriptor);
            }
            services.AddSingleton<ICacheService, TestMemoryCacheService>();

            // 5. Build Service Provider and Seed Test Data
            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<OrderFlowDbContext>();

            db.Database.EnsureDeleted();
            db.Database.EnsureCreated();

            // Seed customers
            if (!db.Customers.Any())
            {
                db.Customers.AddRange(
                    new Customer(1, "Test Customer 1"),
                    new Customer(2, "Test Customer 2")
                );
            }

            // Seed products
            if (!db.Products.Any())
            {
                db.Products.AddRange(
                    new Product(1, "Test Keyboard", 100m),
                    new Product(2, "Test Mouse", 50m),
                    new Product(3, "Test Monitor", 300m)
                );
            }

            // Seed a sample order & dashboard read model entry
            if (!db.Orders.Any())
            {
                var sampleOrder = Order.Create(1, new List<OrderItem>
                {
                    new("Test Keyboard", 1, 100m)
                });
                db.Orders.Add(sampleOrder);
                db.SaveChanges();

                var sampleReadModel = new OrderDashboardReadModel
                {
                    OrderId = sampleOrder.Id,
                    CustomerName = "Test Customer 1",
                    ItemCount = 1,
                    Total = sampleOrder.Total,
                    Status = sampleOrder.Status.ToString(),
                    CreatedAt = sampleOrder.CreatedAt
                };
                db.OrderDashboardReadModels.Add(sampleReadModel);
            }

            db.SaveChanges();
        });
    }
}
