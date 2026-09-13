using MediatR;
using OrderFlow.Application.DTOs;
using OrderFlow.Application.Interfaces;

namespace OrderFlow.Application.Features.Orders.GetDashboardOrders;

public class GetDashboardOrdersHandler : IRequestHandler<GetDashboardOrdersQuery, List<OrderDashboardDto>>
{
    private readonly IOrderDashboardRepository _dashboardRepository;

    public GetDashboardOrdersHandler(IOrderDashboardRepository dashboardRepository)
    {
        _dashboardRepository = dashboardRepository;
    }

    public async Task<List<OrderDashboardDto>> Handle(GetDashboardOrdersQuery request, CancellationToken cancellationToken)
    {
        // Reads strictly from the OrderDashboardReadModels table via the repository
        // Never rebuilds from transactional tables (Orders, OrderItems, Customers) on request
        return await _dashboardRepository.GetDashboardOrdersAsync(cancellationToken);
    }
}
