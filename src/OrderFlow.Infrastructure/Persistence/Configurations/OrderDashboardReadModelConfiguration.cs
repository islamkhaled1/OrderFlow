using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderFlow.Infrastructure.ReadModels;

namespace OrderFlow.Infrastructure.Persistence.Configurations;

public class OrderDashboardReadModelConfiguration : IEntityTypeConfiguration<OrderDashboardReadModel>
{
    public void Configure(EntityTypeBuilder<OrderDashboardReadModel> builder)
    {
        builder.ToTable("OrderDashboardReadModels");

        // Primary key is the OrderId, which corresponds to the transactional Order ID
        builder.HasKey(r => r.OrderId);

        builder.Property(r => r.OrderId)
            .ValueGeneratedNever();

        builder.Property(r => r.CustomerName)
            .IsRequired()
            .HasMaxLength(200);

        builder.Property(r => r.ItemCount)
            .IsRequired();

        builder.Property(r => r.Total)
            .HasPrecision(18, 2)
            .IsRequired();

        builder.Property(r => r.Status)
            .IsRequired()
            .HasMaxLength(50);

        builder.Property(r => r.CreatedAt)
            .IsRequired();

        // Read optimization indexes
        builder.HasIndex(r => r.Status)
            .HasDatabaseName("IX_OrderDashboardReadModels_Status");

        builder.HasIndex(r => r.CreatedAt)
            .HasDatabaseName("IX_OrderDashboardReadModels_CreatedAt");
    }
}
