using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using OrderFlow.Application.Interfaces;
using OrderFlow.Infrastructure.Persistence;
using OrderFlow.Infrastructure.ReadModels;

namespace OrderFlow.Infrastructure.Services;

/// <summary>
/// =========================================================================
/// READ MODEL REFRESH SERVICE
/// =========================================================================
/// Rebuilds/refreshes the 'OrderDashboardReadModels' table (Materialized View)
/// from the authoritative transactional tables (Orders, OrderItems, Customers).
/// 
/// NOTE:
/// The read model table is strictly optimized for fast, denormalized reads
/// by the dashboard endpoint and is NEVER treated as the source of truth.
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

    public async Task RefreshDashboardAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("Refreshing OrderDashboardReadModels materialized view from transactional tables...");

        try
        {
            // 1. Read transactional order data with joins
            var transactionalOrders = await _context.Orders
                .AsNoTracking()
                .Include(o => o.Customer)
                .Include(o => o.Items)
                .ToListAsync(cancellationToken);

            // 2. Project into denormalized read model entries
            var freshReadModels = transactionalOrders.Select(o => new OrderDashboardReadModel
            {
                OrderId = o.Id,
                CustomerName = o.Customer?.Name ?? "Unknown",
                ItemCount = o.Items.Count,
                Total = o.Total,
                Status = o.Status.ToString(),
                CreatedAt = o.CreatedAt
            }).ToList();

            // 3. Synchronize OrderDashboardReadModels table
            var existingReadModels = await _context.OrderDashboardReadModels.ToListAsync(cancellationToken);
            _context.OrderDashboardReadModels.RemoveRange(existingReadModels);
            await _context.OrderDashboardReadModels.AddRangeAsync(freshReadModels, cancellationToken);

            await _context.SaveChangesAsync(cancellationToken);
            _logger.LogInformation("OrderDashboardReadModels refreshed successfully. Total entries: {Count}", freshReadModels.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to refresh OrderDashboardReadModels.");
            throw;
        }
    }
}
