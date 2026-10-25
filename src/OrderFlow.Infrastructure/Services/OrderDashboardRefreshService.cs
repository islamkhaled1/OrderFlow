using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OrderFlow.Application.Interfaces;
using OrderFlow.Infrastructure.Persistence;
using OrderFlow.Infrastructure.ReadModels;

namespace OrderFlow.Infrastructure.Services;

/// <summary>
/// =========================================================================
/// READ MODEL MAINTENANCE
/// =========================================================================
/// Keeps 'OrderDashboardReadModels' (a denormalized table, NEVER the source of truth) in sync with the
/// transactional tables.
///  - <see cref="UpsertOrderAsync"/> is the normal path: driven by outbox events, one row per event,
///    recomputed from current state, so redelivery and reordering are harmless.
///  - <see cref="RefreshDashboardAsync"/> is the rebuild/reconcile path (admin-triggered): a set-based
///    delete + insert in one transaction.
/// Consistency model: eventually consistent; lag = outbox polling interval (default 2s) plus retries.
/// =========================================================================
/// </summary>
public class OrderDashboardRefreshService : IOrderDashboardRefreshService
{
    private readonly OrderFlowDbContext _context;
    private readonly ILogger<OrderDashboardRefreshService> _logger;

    public OrderDashboardRefreshService(
        OrderFlowDbContext context,
        ILogger<OrderDashboardRefreshService> logger)
    {
        _context = context;
        _logger = logger;
    }

    public async Task UpsertOrderAsync(int orderId, CancellationToken cancellationToken = default)
    {
        var source = await _context.Orders
            .AsNoTracking()
            .Where(o => o.Id == orderId)
            .Select(o => new
            {
                CustomerName = o.Customer!.Name,
                ItemCount = o.Items.Count,
                o.Total,
                o.Status,
                o.CreatedAt
            })
            .FirstOrDefaultAsync(cancellationToken);

        var existing = await _context.OrderDashboardReadModels
            .FirstOrDefaultAsync(r => r.OrderId == orderId, cancellationToken);

        if (source is null)
        {
            if (existing is not null) _context.OrderDashboardReadModels.Remove(existing);
        }
        else if (existing is null)
        {
            _context.OrderDashboardReadModels.Add(new OrderDashboardReadModel
            {
                OrderId = orderId,
                CustomerName = source.CustomerName,
                ItemCount = source.ItemCount,
                Total = source.Total,
                Status = source.Status.ToString(),
                CreatedAt = source.CreatedAt
            });
        }
        else
        {
            existing.CustomerName = source.CustomerName;
            existing.ItemCount = source.ItemCount;
            existing.Total = source.Total;
            existing.Status = source.Status.ToString();
            existing.CreatedAt = source.CreatedAt;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    public async Task RefreshDashboardAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Rebuilding OrderDashboardReadModels from transactional tables...");

        var strategy = _context.Database.CreateExecutionStrategy();
        await strategy.ExecuteAsync(async ct =>
        {
            await using var tx = await _context.Database.BeginTransactionAsync(ct);

            await _context.Database.ExecuteSqlRawAsync("DELETE FROM OrderDashboardReadModels", ct);
            await _context.Database.ExecuteSqlRawAsync("""
                INSERT INTO OrderDashboardReadModels (OrderId, CustomerName, ItemCount, Total, Status, CreatedAt)
                SELECT o.Id, c.Name, (SELECT COUNT(*) FROM OrderItems i WHERE i.OrderId = o.Id), o.Total, o.Status, o.CreatedAt
                FROM Orders o
                JOIN Customers c ON c.Id = o.CustomerId
                """, ct);

            await tx.CommitAsync(ct);
        }, cancellationToken);

        _logger.LogInformation("OrderDashboardReadModels rebuilt.");
    }
}
