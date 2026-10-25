using MediatR;
using OrderFlow.Application.DTOs;

namespace OrderFlow.Application.Features.Orders.CreateOrder;

/// <summary>
/// The customer is resolved from <see cref="Interfaces.ICurrentUser"/> inside the handler, never from the payload.
/// </summary>
public record CreateOrderCommand(
    List<OrderItemRequestDto> Items,
    string? IdempotencyKey = null
) : IRequest<CreateOrderResult>;

/// <param name="Replayed">True when the response was reproduced from a previous request with the same Idempotency-Key.</param>
public record CreateOrderResult(CreateOrderResponse Response, bool Replayed);
