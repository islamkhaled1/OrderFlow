namespace OrderFlow.Application.DTOs;

public record OrderDetailsDto(
    int OrderId,
    int CustomerId,
    string CustomerName,
    DateTime CreatedAt,
    string Status,
    decimal Total,
    List<OrderItemDto> Items
);

public record OrderItemDto(
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal LineTotal
);
