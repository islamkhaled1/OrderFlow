using MediatR;
using OrderFlow.Application.DTOs;

namespace OrderFlow.Application.Features.Orders.CreateOrder;

public record CreateOrderCommand(
    int CustomerId,
    List<OrderItemRequestDto> Items
) : IRequest<CreateOrderResponse>;
