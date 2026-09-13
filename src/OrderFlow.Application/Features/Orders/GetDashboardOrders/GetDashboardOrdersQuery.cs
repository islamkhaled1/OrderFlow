using MediatR;
using OrderFlow.Application.DTOs;

namespace OrderFlow.Application.Features.Orders.GetDashboardOrders;

public record GetDashboardOrdersQuery : IRequest<List<OrderDashboardDto>>;
