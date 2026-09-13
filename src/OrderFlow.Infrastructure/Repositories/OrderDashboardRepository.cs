using Microsoft.EntityFrameworkCore;
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

    public async Task<List<OrderDashboardDto>> GetDashboardOrdersAsync(CancellationToken cancellationToken = default)
    {
        // Strictly reads from the read-optimized OrderDashboardReadModels table
        // No expensive joins across transactional tables during user request
        return await _context.OrderDashboardReadModels
            .AsNoTracking()
            .OrderByDescending(r => r.CreatedAt)
            .Select(r => new OrderDashboardDto(
                r.OrderId,
                r.CustomerName,
                r.ItemCount,
                r.Total,
                r.Status,
                r.CreatedAt
            ))
            .ToListAsync(cancellationToken);
    }
}
