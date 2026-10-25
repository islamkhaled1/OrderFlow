using OrderFlow.Application.Common.Models;
using OrderFlow.Application.DTOs;
using OrderFlow.Domain.Entities;
using OrderFlow.Domain.Enums;

namespace OrderFlow.Application.Interfaces;

public sealed record OrderListFilter(
    int Page,
    int PageSize,
    int? CustomerId,
    OrderStatus? Status,
    DateTime? FromUtc,
    DateTime? ToUtc);

public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Read-only projection source (no tracking) including customer and items.</summary>
    Task<Order?> GetByIdWithDetailsAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Tracked aggregate (with items) for state transitions.</summary>
    Task<Order?> GetByIdForUpdateAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Server-side filtered, stably sorted (CreatedAt desc, Id desc) page.</summary>
    Task<PagedResult<OrderListItemDto>> GetPagedAsync(OrderListFilter filter, CancellationToken cancellationToken = default);

    Task<List<int>> GetStalePendingOrderIdsAsync(DateTime createdBeforeUtc, int take, CancellationToken cancellationToken = default);

    Task AddAsync(Order order, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
