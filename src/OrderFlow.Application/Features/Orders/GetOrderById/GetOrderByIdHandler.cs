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
    private readonly ICurrentUser _currentUser;
    private readonly IOptions<CachingOptions> _cachingOptions;
    private readonly ILogger<GetOrderByIdHandler> _logger;

    public GetOrderByIdHandler(
        IOrderRepository orderRepository,
        ICacheService cacheService,
        ICurrentUser currentUser,
        IOptions<CachingOptions> cachingOptions,
        ILogger<GetOrderByIdHandler> logger)
    {
        _orderRepository = orderRepository;
        _cacheService = cacheService;
        _currentUser = currentUser;
        _cachingOptions = cachingOptions;
        _logger = logger;
    }

    public async Task<OrderDetailsDto> Handle(GetOrderByIdQuery request, CancellationToken cancellationToken)
    {
        var cacheKey = $"order:{request.Id}";

        // 1. Cache-aside read. The cache is shared by all callers, so it is never an authorization
        //    source: ownership is checked below against the (cached or fresh) CustomerId.
        //    ICacheService already treats expected Redis outages as a miss (and records them).
        var order = await _cacheService.GetAsync<OrderDetailsDto>(cacheKey, cancellationToken);

        if (order != null)
        {
            _logger.LogDebug("Cache hit for order {OrderId}", request.Id);
        }
        else
        {
            _logger.LogDebug("Cache miss for order {OrderId}. Fetching from database.", request.Id);
            order = await LoadFromDatabaseAsync(request.Id, cancellationToken);

            var expirationMinutes = _cachingOptions.Value.OrderDetailsExpirationMinutes > 0
                ? _cachingOptions.Value.OrderDetailsExpirationMinutes
                : 5;
            await _cacheService.SetAsync(cacheKey, order, TimeSpan.FromMinutes(expirationMinutes), cancellationToken);
        }

        // 2. Authorization: customers see only their own orders, admins see all.
        //    Other customers' orders are reported as 404 so order ids cannot be enumerated.
        if (!_currentUser.IsAdmin && order.CustomerId != _currentUser.CustomerId)
        {
            throw new NotFoundException(nameof(Order), request.Id);
        }

        return order;
    }

    private async Task<OrderDetailsDto> LoadFromDatabaseAsync(int id, CancellationToken cancellationToken)
    {
        var order = await _orderRepository.GetByIdWithDetailsAsync(id, cancellationToken)
            ?? throw new NotFoundException(nameof(Order), id);

        return new OrderDetailsDto(
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
            )).ToList());
    }
}
