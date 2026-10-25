using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using OrderFlow.Domain.Entities;
using OrderFlow.Infrastructure.Persistence.Entities;

namespace OrderFlow.Infrastructure.Persistence.Configurations;

public class AppUserConfiguration : IEntityTypeConfiguration<AppUser>
{
    public void Configure(EntityTypeBuilder<AppUser> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();

        builder.Property(u => u.Email).IsRequired().HasMaxLength(256);
        builder.Property(u => u.PasswordHash).IsRequired().HasMaxLength(512);
        builder.Property(u => u.Role).IsRequired().HasMaxLength(50);

        builder.HasIndex(u => u.Email).IsUnique().HasDatabaseName("UX_Users_Email");

        builder.HasOne(u => u.Customer)
            .WithMany()
            .HasForeignKey(u => u.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        // One account per customer.
        builder.HasIndex(u => u.CustomerId)
            .IsUnique()
            .HasFilter("[CustomerId] IS NOT NULL")
            .HasDatabaseName("UX_Users_CustomerId");
    }
}

public class OutboxMessageConfiguration : IEntityTypeConfiguration<OutboxMessage>
{
    public void Configure(EntityTypeBuilder<OutboxMessage> builder)
    {
        builder.ToTable("OutboxMessages");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();

        builder.Property(m => m.EventType).IsRequired().HasMaxLength(100);
        builder.Property(m => m.Payload).IsRequired();
        builder.Property(m => m.LastError).HasMaxLength(2000);

        // Work-queue index: only unprocessed, non-dead-lettered rows are indexed, so the index stays
        // small no matter how many processed rows are still retained.
        builder.HasIndex(m => m.NextAttemptAtUtc)
            .HasFilter("[ProcessedAtUtc] IS NULL AND [DeadLetteredAtUtc] IS NULL")
            .HasDatabaseName("IX_OutboxMessages_Pending");

        builder.HasIndex(m => m.ProcessedAtUtc)
            .HasFilter("[ProcessedAtUtc] IS NOT NULL")
            .HasDatabaseName("IX_OutboxMessages_Processed");

        builder.HasIndex(m => m.DeadLetteredAtUtc)
            .HasFilter("[DeadLetteredAtUtc] IS NOT NULL")
            .HasDatabaseName("IX_OutboxMessages_DeadLettered");
    }
}

public class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable("IdempotencyRecords");
        builder.HasKey(r => r.Id);

        builder.Property(r => r.Scope).IsRequired().HasMaxLength(64);
        builder.Property(r => r.Key).IsRequired().HasMaxLength(128);
        builder.Property(r => r.RequestHash).IsRequired().HasMaxLength(64).IsFixedLength();

        // The database constraint, not application code, guarantees one record per (caller, key).
        builder.HasIndex(r => new { r.Scope, r.Key })
            .IsUnique()
            .HasDatabaseName("UX_IdempotencyRecords_Scope_Key");

        builder.HasIndex(r => r.ExpiresAtUtc)
            .HasDatabaseName("IX_IdempotencyRecords_ExpiresAtUtc");
    }
}
