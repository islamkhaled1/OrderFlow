using Microsoft.EntityFrameworkCore;
using OrderFlow.Application.Common.Models;
using OrderFlow.Application.DTOs;
using OrderFlow.Application.Interfaces;
using OrderFlow.Infrastructure.Persistence;

namespace OrderFlow.Infrastructure.Repositories;

public class OrderDashboardRepository : IOrderDashboardRepository
{
    private readonly OrderFlowDbContext _context;

    public OrderDashboardRepository(OrderFlowDbContext context)
    {
        _context = context;
    }

    public async Task<PagedResult<OrderDashboardDto>> GetDashboardOrdersAsync(
        int page, int pageSize, string? status, CancellationToken cancellationToken = default)
    {
        // Strictly reads the read-optimized table; no joins across transactional tables.
        var query = _context.OrderDashboardReadModels.AsNoTracking().AsQueryable();
        if (!string.IsNullOrEmpty(status))
            query = query.Where(r => r.Status == status);

        var total = await query.CountAsync(cancellationToken);

        var items = await query
            .OrderByDescending(r => r.CreatedAt)
            .ThenByDescending(r => r.OrderId)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(r => new OrderDashboardDto(r.OrderId, r.CustomerName, r.ItemCount, r.Total, r.Status, r.CreatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<OrderDashboardDto>(items, page, pageSize, total);
    }
}
