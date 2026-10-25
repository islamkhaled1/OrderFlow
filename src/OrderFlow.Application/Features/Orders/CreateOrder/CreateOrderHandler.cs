using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using MediatR;
using OrderFlow.Application.Common.Exceptions;
using OrderFlow.Application.DTOs;
using OrderFlow.Application.Interfaces;
using OrderFlow.Domain.Entities;

namespace OrderFlow.Application.Features.Orders.CreateOrder;

/// <summary>
/// Creates an order atomically: (optional) idempotency claim + stock reservation + order + outbox
/// message all commit together or not at all.
/// </summary>
public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, CreateOrderResult>
{
    private readonly ICurrentUser _currentUser;
    private readonly ICustomerRepository _customerRepository;
    private readonly IProductRepository _productRepository;
    private readonly IOrderRepository _orderRepository;
    private readonly IIdempotencyStore _idempotencyStore;
    private readonly IOutbox _outbox;
    private readonly IUnitOfWork _unitOfWork;

    public CreateOrderHandler(
        ICurrentUser currentUser,
        ICustomerRepository customerRepository,
        IProductRepository productRepository,
        IOrderRepository orderRepository,
        IIdempotencyStore idempotencyStore,
        IOutbox outbox,
        IUnitOfWork unitOfWork)
    {
        _currentUser = currentUser;
        _customerRepository = customerRepository;
        _productRepository = productRepository;
        _orderRepository = orderRepository;
        _idempotencyStore = idempotencyStore;
        _outbox = outbox;
        _unitOfWork = unitOfWork;
    }

    public async Task<CreateOrderResult> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        // The order owner always comes from the authenticated identity, never from the payload.
        var customerId = _currentUser.CustomerId
            ?? throw new ForbiddenException("Only customer accounts can place orders.");

        // One line per product keeps stock reservation and totals consistent; sorted by id so that
        // concurrent orders always lock product rows in the same order (avoids deadlocks).
        var lines = request.Items
            .GroupBy(i => i.ProductId)
            .Select(g => new { ProductId = g.Key, Quantity = g.Sum(i => i.Quantity) })
            .OrderBy(l => l.ProductId)
            .ToList();

        var idempotencyKey = request.IdempotencyKey?.Trim();
        var scope = $"customer:{customerId}";
        var requestHash = ComputeRequestHash(lines.Select(l => (l.ProductId, l.Quantity)));

        return await _unitOfWork.ExecuteInTransactionAsync(async token =>
        {
            // 1. Idempotency: claim the key inside the same transaction as the order.
            if (idempotencyKey is not null &&
                !await _idempotencyStore.TryClaimAsync(scope, idempotencyKey, requestHash, token))
            {
                return await ReplayAsync(scope, idempotencyKey, requestHash, token);
            }

            // 2. Customer must exist.
            if (!await _customerRepository.ExistsAsync(customerId, token))
            {
                throw new NotFoundException(nameof(Customer), customerId);
            }

            // 3. Products must exist and be orderable; price and name come from the server.
            var productIds = lines.Select(l => l.ProductId).ToList();
            var products = (await _productRepository.GetByIdsAsync(productIds, token)).ToDictionary(p => p.Id);

            var missing = productIds.Where(id => !products.ContainsKey(id)).ToList();
            if (missing.Count != 0)
            {
                throw new NotFoundException($"The following product IDs were not found: {string.Join(", ", missing)}");
            }

            var inactive = lines.Where(l => !products[l.ProductId].IsActive)
                .Select(l => new StockShortage(l.ProductId, l.Quantity)).ToList();
            if (inactive.Count != 0)
            {
                throw new InsufficientStockException(inactive);
            }

            // 4. Reserve stock with atomic conditional updates. Any failure throws, which rolls back
            //    every reservation made so far (failed orders never reduce inventory).
            foreach (var line in lines)
            {
                if (!await _productRepository.TryReserveStockAsync(line.ProductId, line.Quantity, token))
                {
                    throw new InsufficientStockException(new[] { new StockShortage(line.ProductId, line.Quantity) });
                }
            }

            // 5. Build the aggregate with product name/price snapshots.
            var orderItems = lines.Select(l =>
            {
                var product = products[l.ProductId];
                return new OrderItem(product.Id, product.Name, l.Quantity, product.Price);
            }).ToList();

            var order = Order.Create(customerId, orderItems);
            await _orderRepository.AddAsync(order, token);

            // 6. Persist order and outbox row; the generated order id is needed for the event payload,
            //    so the outbox row is added after the first save and flushed by the second.
            await _orderRepository.SaveChangesAsync(token);
            _outbox.Enqueue(OutboxEventTypes.OrderCreated, new OrderEventPayload(order.Id, order.CustomerId));

            var response = new CreateOrderResponse(
                order.Id, order.CustomerId, order.Total, order.Status.ToString(), order.CreatedAt);

            if (idempotencyKey is not null)
            {
                await _idempotencyStore.CompleteAsync(
                    scope, idempotencyKey, 201, JsonSerializer.Serialize(response), token);
            }

            await _orderRepository.SaveChangesAsync(token);
            return new CreateOrderResult(response, Replayed: false);
        }, cancellationToken);
    }

    private async Task<CreateOrderResult> ReplayAsync(string scope, string key, string requestHash, CancellationToken token)
    {
        var existing = await _idempotencyStore.GetAsync(scope, key, token);
        if (existing is null || existing.ResponseBody is null)
        {
            // Cannot normally happen (claim and completion commit atomically); fail safe, never duplicate.
            throw new ConflictException("A request with this Idempotency-Key is still being processed. Retry shortly.");
        }

        if (!string.Equals(existing.RequestHash, requestHash, StringComparison.Ordinal))
        {
            throw new IdempotencyKeyReuseException();
        }

        var original = JsonSerializer.Deserialize<CreateOrderResponse>(existing.ResponseBody)
            ?? throw new ConflictException("The stored response for this Idempotency-Key could not be read.");

        return new CreateOrderResult(original, Replayed: true);
    }

    /// <summary>
    /// Canonical SHA-256 of the order payload: product lines merged and sorted, so semantically identical
    /// requests (item order, split duplicates) hash identically.
    /// </summary>
    public static string ComputeRequestHash(IEnumerable<(int ProductId, int Quantity)> mergedLines)
    {
        var canonical = string.Join("|", mergedLines
            .OrderBy(l => l.ProductId)
            .Select(l => $"{l.ProductId}:{l.Quantity}"));
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)));
    }
}
