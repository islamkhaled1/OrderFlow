namespace OrderFlow.Application.DTOs;

public record OrderDashboardDto(
    int OrderId,
    string CustomerName,
    int ItemCount,
    decimal Total,
    string Status,
    DateTime CreatedAt
);
