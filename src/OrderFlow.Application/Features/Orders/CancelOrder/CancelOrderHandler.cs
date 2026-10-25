using MediatR;
using OrderFlow.Application.Interfaces;
using OrderFlow.Application.Services;

namespace OrderFlow.Application.Features.Orders.CancelOrder;

public record CancelOrderCommand(int OrderId, string? Reason) : IRequest;

public class CancelOrderHandler : IRequestHandler<CancelOrderCommand>
{
    private readonly ICurrentUser _currentUser;
    private readonly OrderCancellationService _cancellation;

    public CancelOrderHandler(ICurrentUser currentUser, OrderCancellationService cancellation)
    {
        _currentUser = currentUser;
        _cancellation = cancellation;
    }

    public async Task Handle(CancelOrderCommand request, CancellationToken cancellationToken)
    {
        // Customers can cancel only their own orders; admins any order.
        int? ownerFilter = _currentUser.IsAdmin ? null : _currentUser.CustomerId ?? -1;
        var reason = string.IsNullOrWhiteSpace(request.Reason) ? "Cancelled by request." : request.Reason!;

        await _cancellation.CancelAsync(request.OrderId, reason, ownerFilter, cancellationToken);
    }
}
