namespace OrderFlow.Application.DTOs;

public record OrderListItemDto(
    int OrderId,
    int CustomerId,
    string CustomerName,
    decimal Total,
    string Status,
    DateTime CreatedAt
);
