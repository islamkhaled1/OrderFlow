using OrderFlow.Domain.Entities;

namespace OrderFlow.Application.Interfaces;

public interface IOrderRepository
{
    Task<Order?> GetByIdAsync(int id, CancellationToken cancellationToken = default);

    Task<Order?> GetByIdWithDetailsAsync(int id, CancellationToken cancellationToken = default);

    Task<List<Order>> GetOrdersReadOnlyAsync(CancellationToken cancellationToken = default);

    Task<List<Order>> GetPendingOrdersAsync(CancellationToken cancellationToken = default);

    Task AddAsync(Order order, CancellationToken cancellationToken = default);

    Task SaveChangesAsync(CancellationToken cancellationToken = default);
}
