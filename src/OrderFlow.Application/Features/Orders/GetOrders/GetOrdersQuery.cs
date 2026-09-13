using MediatR;
using OrderFlow.Application.DTOs;

namespace OrderFlow.Application.Features.Orders.GetOrders;

public record GetOrdersQuery : IRequest<List<OrderListItemDto>>;
