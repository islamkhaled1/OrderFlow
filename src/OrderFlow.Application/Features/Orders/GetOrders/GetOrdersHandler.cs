using MediatR;
using OrderFlow.Application.DTOs;
using OrderFlow.Application.Interfaces;

namespace OrderFlow.Application.Features.Orders.GetOrders;

public class GetOrdersHandler : IRequestHandler<GetOrdersQuery, List<OrderListItemDto>>
{
    private readonly IOrderRepository _orderRepository;

    public GetOrdersHandler(IOrderRepository orderRepository)
    {
        _orderRepository = orderRepository;
    }

    public async Task<List<OrderListItemDto>> Handle(GetOrdersQuery request, CancellationToken cancellationToken)
    {
        var orders = await _orderRepository.GetOrdersReadOnlyAsync(cancellationToken);

        return orders.Select(order => new OrderListItemDto(
            order.Id,
            order.Customer?.Name ?? string.Empty,
            order.Total,
            order.Status.ToString(),
            order.CreatedAt
        )).ToList();
    }
}
