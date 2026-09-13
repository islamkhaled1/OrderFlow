using OrderFlow.Application.DTOs;

namespace OrderFlow.Application.Interfaces;

public interface IOrderDashboardRepository
{
    Task<List<OrderDashboardDto>> GetDashboardOrdersAsync(CancellationToken cancellationToken = default);
}
