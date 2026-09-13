namespace OrderFlow.Application.DTOs;

public record CreateOrderResponse(
    int OrderId,
    int CustomerId,
    decimal Total,
    string Status,
    DateTime CreatedAt
);
