using MediatR;
using OrderFlow.Application.Common.Exceptions;
using OrderFlow.Application.Interfaces;
using OrderFlow.Domain.Entities;

namespace OrderFlow.Application.Features.Orders.CompleteOrder;

public record CompleteOrderCommand(int OrderId) : IRequest;

/// <summary>
/// Pending -> Completed. This simulates fulfilment: an administrator explicitly completes the order.
/// Nothing completes automatically on a timer. Stock stays deducted (the reservation is consumed).
/// </summary>
public class CompleteOrderHandler : IRequestHandler<CompleteOrderCommand>
{
    private readonly ICurrentUser _currentUser;
    private readonly IOrderRepository _orderRepository;
    private readonly IOutbox _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICacheService _cache;

    public CompleteOrderHandler(
        ICurrentUser currentUser,
        IOrderRepository orderRepository,
        IOutbox outbox,
        IUnitOfWork unitOfWork,
        ICacheService cache)
    {
        _currentUser = currentUser;
        _orderRepository = orderRepository;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _cache = cache;
    }

    public async Task Handle(CompleteOrderCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAdmin)
            throw new ForbiddenException("Only administrators can complete orders.");

        await _unitOfWork.ExecuteInTransactionAsync<bool>(async token =>
        {
            var order = await _orderRepository.GetByIdForUpdateAsync(request.OrderId, token)
                ?? throw new NotFoundException(nameof(Order), request.OrderId);

            order.MarkAsCompleted();
            _outbox.Enqueue(OutboxEventTypes.OrderCompleted, new OrderEventPayload(order.Id, order.CustomerId));
            await _orderRepository.SaveChangesAsync(token);
            return true;
        }, cancellationToken);

        await _cache.RemoveAsync($"order:{request.OrderId}", cancellationToken);
    }
}
