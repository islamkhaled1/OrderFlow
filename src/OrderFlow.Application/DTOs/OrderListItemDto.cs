namespace OrderFlow.Application.DTOs;

public record OrderListItemDto(
    int OrderId,
    string CustomerName,
    decimal Total,
    string Status,
    DateTime CreatedAt
);
