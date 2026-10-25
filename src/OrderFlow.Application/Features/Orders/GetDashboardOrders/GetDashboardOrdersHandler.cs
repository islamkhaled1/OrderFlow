using MediatR;
using OrderFlow.Application.Common.Models;
using OrderFlow.Application.DTOs;
using OrderFlow.Application.Interfaces;
using OrderFlow.Domain.Enums;

namespace OrderFlow.Application.Features.Orders.GetDashboardOrders;

public class GetDashboardOrdersHandler : IRequestHandler<GetDashboardOrdersQuery, PagedResult<OrderDashboardDto>>
{
    private readonly IOrderDashboardRepository _dashboardRepository;

    public GetDashboardOrdersHandler(IOrderDashboardRepository dashboardRepository)
    {
        _dashboardRepository = dashboardRepository;
    }

    public Task<PagedResult<OrderDashboardDto>> Handle(GetDashboardOrdersQuery request, CancellationToken cancellationToken)
    {
        // Reads strictly from the OrderDashboardReadModels table; never joins transactional tables.
        var status = request.Status is null
            ? null
            : Enum.Parse<OrderStatus>(request.Status, ignoreCase: true).ToString();

        return _dashboardRepository.GetDashboardOrdersAsync(request.Page, request.PageSize, status, cancellationToken);
    }
}
