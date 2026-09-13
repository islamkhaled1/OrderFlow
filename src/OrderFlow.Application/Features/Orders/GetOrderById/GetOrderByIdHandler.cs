using MediatR;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderFlow.Application.Common.Exceptions;
using OrderFlow.Application.Common.Options;
using OrderFlow.Application.DTOs;
using OrderFlow.Application.Interfaces;
using OrderFlow.Domain.Entities;

namespace OrderFlow.Application.Features.Orders.GetOrderById;

public class GetOrderByIdHandler : IRequestHandler<GetOrderByIdQuery, OrderDetailsDto>
{
    private readonly IOrderRepository _orderRepository;
    private readonly ICacheService _cacheService;
    private readonly IOptions<CachingOptions> _cachingOptions;
    private readonly ILogger<GetOrderByIdHandler> _logger;

    public GetOrderByIdHandler(
        IOrderRepository orderRepository,
        ICacheService cacheService,
        IOptions<CachingOptions> cachingOptions,
        ILogger<GetOrderByIdHandler> logger)
    {
        _orderRepository = orderRepository;
        _cacheService = cacheService;
        _cachingOptions = cachingOptions;
        _logger = logger;
    }

    public async Task<OrderDetailsDto> Handle(GetOrderByIdQuery request, CancellationToken cancellationToken)
    {
        var cacheKey = $"order:{request.Id}";

        // 1. Check Redis cache
        try
        {
            var cachedOrder = await _cacheService.GetAsync<OrderDetailsDto>(cacheKey, cancellationToken);
            if (cachedOrder != null)
            {
                _logger.LogInformation("Cache hit for order {OrderId}", request.Id);
                return cachedOrder;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve order {OrderId} from cache. Falling back to database.", request.Id);
        }

        // 2. Cache miss or cache failure: Query transactional database
        _logger.LogInformation("Cache miss for order {OrderId}. Fetching from database.", request.Id);
        var order = await _orderRepository.GetByIdWithDetailsAsync(request.Id, cancellationToken);
        if (order == null)
        {
            throw new NotFoundException(nameof(Order), request.Id);
        }

        // 3. Map to DTO
        var orderDto = new OrderDetailsDto(
            order.Id,
            order.CustomerId,
            order.Customer?.Name ?? string.Empty,
            order.CreatedAt,
            order.Status.ToString(),
            order.Total,
            order.Items.Select(item => new OrderItemDto(
                item.ProductName,
                item.Quantity,
                item.UnitPrice,
                item.LineTotal
            )).ToList()
        );

        // 4. Store in Redis cache with configured expiration
        try
        {
            var expirationMinutes = _cachingOptions.Value.OrderDetailsExpirationMinutes > 0
                ? _cachingOptions.Value.OrderDetailsExpirationMinutes
                : 5;

            await _cacheService.SetAsync(
                cacheKey,
                orderDto,
                TimeSpan.FromMinutes(expirationMinutes),
                cancellationToken);

            _logger.LogInformation("Order {OrderId} stored in cache with TTL {TTL} minutes", request.Id, expirationMinutes);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to cache order {OrderId}. Continuing with database response.", request.Id);
        }

        return orderDto;
    }
}
