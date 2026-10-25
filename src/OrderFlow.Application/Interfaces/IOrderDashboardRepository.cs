using OrderFlow.Application.Common.Models;
using OrderFlow.Application.DTOs;

namespace OrderFlow.Application.Interfaces;

public interface IOrderDashboardRepository
{
    Task<PagedResult<OrderDashboardDto>> GetDashboardOrdersAsync(
        int page, int pageSize, string? status, CancellationToken cancellationToken = default);
}
