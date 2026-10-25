namespace OrderFlow.Application.DTOs;

public record RegisterRequest(string Email, string Password, string Name);

public record LoginRequest(string Email, string Password);

public record AuthResponse(string AccessToken, DateTime ExpiresAtUtc, string Role, int? CustomerId);

public record CancelOrderRequest(string? Reason);
