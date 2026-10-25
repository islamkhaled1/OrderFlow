namespace OrderFlow.Application.DTOs;

/// <summary>
/// Client payload for order creation. The customer is never supplied by the client — it is derived
/// from the authenticated user — and neither prices nor totals are accepted.
/// </summary>
public record CreateOrderRequest(
    List<OrderItemRequestDto> Items
);

public record OrderItemRequestDto(
    int ProductId,
    int Quantity
);
