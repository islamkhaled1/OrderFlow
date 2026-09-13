using MediatR;
using OrderFlow.Application.DTOs;

namespace OrderFlow.Application.Features.Orders.GetOrderById;

public record GetOrderByIdQuery(int Id) : IRequest<OrderDetailsDto>;
