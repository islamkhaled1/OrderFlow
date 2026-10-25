using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderFlow.Domain.Entities;

namespace OrderFlow.Infrastructure.Persistence.Configurations;

public class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders", t =>
            t.HasCheckConstraint("CK_Orders_Total_NonNegative", "[Total] >= 0"));

        builder.HasKey(o => o.Id);

        builder.Property(o => o.Status)
            .HasConversion<string>()
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(o => o.Total)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(o => o.CreatedAt)
            .IsRequired();

        builder.Property(o => o.CancellationReason)
            .HasMaxLength(500);

        // Optimistic concurrency token: guards state transitions (cancel vs complete vs expire).
        builder.Property(o => o.RowVersion)
            .IsRowVersion();

        // Foreign Key to Customer
        builder.HasOne(o => o.Customer)
            .WithMany(c => c.Orders)
            .HasForeignKey(o => o.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        // Map navigation collection and backfield
        builder.HasMany(o => o.Items)
            .WithOne(i => i.Order)
            .HasForeignKey(i => i.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(o => o.Items)
            .UsePropertyAccessMode(PropertyAccessMode.Field);

        builder.HasIndex(o => o.CustomerId)
            .HasDatabaseName("IX_Orders_CustomerId");

        builder.HasIndex(o => o.Status)
            .HasDatabaseName("IX_Orders_Status");

        builder.HasIndex(o => o.CreatedAt)
            .HasDatabaseName("IX_Orders_CreatedAt");

        // "My orders" listing: filter by customer, sorted by newest first.
        builder.HasIndex(o => new { o.CustomerId, o.CreatedAt, o.Id })
            .HasDatabaseName("IX_Orders_CustomerId_CreatedAt_Id")
            .IsDescending(false, true, true);

        // Expiry job: find stale pending orders by age.
        builder.HasIndex(o => new { o.Status, o.CreatedAt })
            .HasDatabaseName("IX_Orders_Status_CreatedAt");
    }
}
