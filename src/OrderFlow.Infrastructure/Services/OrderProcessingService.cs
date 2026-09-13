using Microsoft.Extensions.Logging;
using OrderFlow.Application.Interfaces;

namespace OrderFlow.Infrastructure.Services;

public class OrderProcessingService : IOrderProcessingService
{
    private readonly IOrderRepository _orderRepository;
    private readonly ICacheService _cacheService;
    private readonly ILogger<OrderProcessingService> _logger;

    public OrderProcessingService(
        IOrderRepository orderRepository,
        ICacheService cacheService,
        ILogger<OrderProcessingService> logger)
    {
        _orderRepository = orderRepository;
        _cacheService = cacheService;
        _logger = logger;
    }

    public async Task<List<int>> ProcessPendingOrdersAsync(CancellationToken cancellationToken = default)
    {
        var pendingOrders = await _orderRepository.GetPendingOrdersAsync(cancellationToken);
        if (pendingOrders.Count == 0)
        {
            _logger.LogDebug("No pending orders found to process.");
            return new List<int>();
        }

        _logger.LogInformation("Processing {Count} pending order(s)...", pendingOrders.Count);
        var processedOrderIds = new List<int>();

        foreach (var order in pendingOrders)
        {
            // Transition status from Pending to Completed
            order.MarkAsCompleted();
            processedOrderIds.Add(order.Id);
        }

        // Save transactional changes
        await _orderRepository.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("Successfully transitioned orders {OrderIds} to Completed.", string.Join(", ", processedOrderIds));

        // Invalidate Redis cache entries for affected orders to prevent stale cached status
        foreach (var orderId in processedOrderIds)
        {
            var cacheKey = $"order:{orderId}";
            await _cacheService.RemoveAsync(cacheKey, cancellationToken);
            _logger.LogInformation("Invalidated cache entry '{CacheKey}' for completed order.", cacheKey);
        }

        return processedOrderIds;
    }
}
