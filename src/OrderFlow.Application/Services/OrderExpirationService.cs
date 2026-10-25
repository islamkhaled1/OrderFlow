using Microsoft.Extensions.Logging;
using OrderFlow.Application.Common.Exceptions;
using OrderFlow.Application.Interfaces;
using OrderFlow.Domain.Exceptions;

namespace OrderFlow.Application.Services;

/// <summary>Cancels stale pending orders so their reserved stock returns to the shelf.</summary>
public class OrderExpirationService : IOrderExpirationService
{
    public const string ExpiredReason = "Expired: not completed within the allowed time.";

    private readonly IOrderRepository _orderRepository;
    private readonly OrderCancellationService _cancellation;
    private readonly ILogger<OrderExpirationService> _logger;

    public OrderExpirationService(
        IOrderRepository orderRepository,
        OrderCancellationService cancellation,
        ILogger<OrderExpirationService> logger)
    {
        _orderRepository = orderRepository;
        _cancellation = cancellation;
        _logger = logger;
    }

    public async Task<int> ExpireStaleOrdersAsync(DateTime createdBeforeUtc, int batchSize, CancellationToken cancellationToken)
    {
        var ids = await _orderRepository.GetStalePendingOrderIdsAsync(createdBeforeUtc, batchSize, cancellationToken);
        var expired = 0;

        foreach (var id in ids)
        {
            try
            {
                await _cancellation.CancelAsync(id, ExpiredReason, onlyForCustomerId: null, cancellationToken);
                expired++;
            }
            catch (Exception ex) when (ex is InvalidOrderStateTransitionException or ConflictException or NotFoundException)
            {
                // Someone completed/cancelled it first: nothing to do, stock was handled by that path.
                _logger.LogInformation("Order {OrderId} was not expired: {Reason}", id, ex.Message);
            }
        }

        return expired;
    }
}
