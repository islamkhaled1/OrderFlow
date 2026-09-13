namespace OrderFlow.Application.DTOs;

public record CreateOrderRequest(
    int CustomerId,
    List<OrderItemRequestDto> Items
);

public record OrderItemRequestDto(
    int ProductId,
    int Quantity
);
