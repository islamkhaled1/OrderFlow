using Microsoft.EntityFrameworkCore;
using OrderFlow.Application.Common.Models;
using OrderFlow.Application.DTOs;
using OrderFlow.Application.Interfaces;
using OrderFlow.Domain.Entities;
using OrderFlow.Domain.Enums;
using OrderFlow.Infrastructure.Persistence;

namespace OrderFlow.Infrastructure.Repositories;

public class OrderRepository : IOrderRepository
{
    private readonly OrderFlowDbContext _context;

    public OrderRepository(OrderFlowDbContext context)
    {
        _context = context;
    }

    public async Task<Order?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Orders.FindAsync(new object[] { id }, cancellationToken);
    }

    public async Task<Order?> GetByIdWithDetailsAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Orders
            .AsNoTracking()
            .Include(o => o.Customer)
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
    }

    public async Task<Order?> GetByIdForUpdateAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.Orders
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
    }

    public async Task<PagedResult<OrderListItemDto>> GetPagedAsync(OrderListFilter filter, CancellationToken cancellationToken = default)
    {
        var query = _context.Orders.AsNoTracking().AsQueryable();

        if (filter.CustomerId.HasValue)
            query = query.Where(o => o.CustomerId == filter.CustomerId.Value);
        if (filter.Status.HasValue)
            query = query.Where(o => o.Status == filter.Status.Value);
        if (filter.FromUtc.HasValue)
            query = query.Where(o => o.CreatedAt >= filter.FromUtc.Value);
        if (filter.ToUtc.HasValue)
            query = query.Where(o => o.CreatedAt <= filter.ToUtc.Value);

        var total = await query.CountAsync(cancellationToken);

        // Stable order: CreatedAt desc with Id as a unique tiebreaker so pages never overlap or skip rows.
        var rows = await query
            .OrderByDescending(o => o.CreatedAt)
            .ThenByDescending(o => o.Id)
            .Skip((filter.Page - 1) * filter.PageSize)
            .Take(filter.PageSize)
            .Select(o => new { o.Id, o.CustomerId, CustomerName = o.Customer!.Name, o.Total, o.Status, o.CreatedAt })
            .ToListAsync(cancellationToken);

        var items = rows
            .Select(r => new OrderListItemDto(r.Id, r.CustomerId, r.CustomerName, r.Total, r.Status.ToString(), r.CreatedAt))
            .ToList();

        return new PagedResult<OrderListItemDto>(items, filter.Page, filter.PageSize, total);
    }

    public async Task<List<int>> GetStalePendingOrderIdsAsync(DateTime createdBeforeUtc, int take, CancellationToken cancellationToken = default)
    {
        return await _context.Orders
            .AsNoTracking()
            .Where(o => o.Status == OrderStatus.Pending && o.CreatedAt < createdBeforeUtc)
            .OrderBy(o => o.CreatedAt)
            .Select(o => o.Id)
            .Take(take)
            .ToListAsync(cancellationToken);
    }

    public async Task AddAsync(Order order, CancellationToken cancellationToken = default)
    {
        await _context.Orders.AddAsync(order, cancellationToken);
    }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        await _context.SaveChangesAsync(cancellationToken);
    }
}
