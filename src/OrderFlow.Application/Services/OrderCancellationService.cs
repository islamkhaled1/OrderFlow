using OrderFlow.Application.Common.Exceptions;
using OrderFlow.Application.Interfaces;
using OrderFlow.Domain.Entities;

namespace OrderFlow.Application.Services;

/// <summary>
/// Cancels a pending order and releases its reserved stock in ONE transaction. The state-machine
/// guard (<see cref="Order.Cancel"/>) plus the rowversion check guarantee that a given order releases
/// its stock at most once, even if cancel/expire/complete requests race.
/// </summary>
public class OrderCancellationService
{
    private readonly IOrderRepository _orderRepository;
    private readonly IProductRepository _productRepository;
    private readonly IOutbox _outbox;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ICacheService _cache;

    public OrderCancellationService(
        IOrderRepository orderRepository,
        IProductRepository productRepository,
        IOutbox outbox,
        IUnitOfWork unitOfWork,
        ICacheService cache)
    {
        _orderRepository = orderRepository;
        _productRepository = productRepository;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
        _cache = cache;
    }

    /// <param name="onlyForCustomerId">When set, the order must belong to this customer (otherwise it is reported as not found).</param>
    public async Task CancelAsync(int orderId, string reason, int? onlyForCustomerId, CancellationToken cancellationToken)
    {
        await _unitOfWork.ExecuteInTransactionAsync<bool>(async token =>
        {
            var order = await _orderRepository.GetByIdForUpdateAsync(orderId, token);
            if (order is null || (onlyForCustomerId.HasValue && order.CustomerId != onlyForCustomerId.Value))
            {
                throw new NotFoundException(nameof(Order), orderId);
            }

            order.Cancel(reason); // throws InvalidOrderStateTransitionException unless Pending

            _outbox.Enqueue(OutboxEventTypes.OrderCancelled, new OrderEventPayload(order.Id, order.CustomerId));

            // Status change first: if another request already moved the order, the rowversion check
            // fails here and nothing has been released yet.
            await _orderRepository.SaveChangesAsync(token);

            foreach (var item in order.Items.Where(i => i.ProductId.HasValue).OrderBy(i => i.ProductId))
            {
                await _productRepository.ReleaseStockAsync(item.ProductId!.Value, item.Quantity, token);
            }

            return true;
        }, cancellationToken);

        // Best-effort immediate invalidation; the outbox message guarantees eventual invalidation.
        await _cache.RemoveAsync($"order:{orderId}", cancellationToken);
    }
}
