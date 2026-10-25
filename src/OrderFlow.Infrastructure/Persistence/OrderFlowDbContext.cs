using Microsoft.EntityFrameworkCore;
using OrderFlow.Domain.Entities;
using OrderFlow.Infrastructure.Persistence.Entities;
using OrderFlow.Infrastructure.ReadModels;

namespace OrderFlow.Infrastructure.Persistence;

public class OrderFlowDbContext : DbContext
{
    public OrderFlowDbContext(DbContextOptions<OrderFlowDbContext> options)
        : base(options)
    {
    }

    // Source of truth: transactional tables
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<AppUser> Users => Set<AppUser>();

    // Reliability infrastructure
    public DbSet<OutboxMessage> OutboxMessages => Set<OutboxMessage>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    // Materialized view / read model: read-optimized table (NOT source of truth)
    public DbSet<OrderDashboardReadModel> OrderDashboardReadModels => Set<OrderDashboardReadModel>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(OrderFlowDbContext).Assembly);
    }
}
