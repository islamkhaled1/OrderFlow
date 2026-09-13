using MediatR;
using OrderFlow.Application.Common.Exceptions;
using OrderFlow.Application.DTOs;
using OrderFlow.Application.Interfaces;
using OrderFlow.Domain.Entities;

namespace OrderFlow.Application.Features.Orders.CreateOrder;

public class CreateOrderHandler : IRequestHandler<CreateOrderCommand, CreateOrderResponse>
{
    private readonly ICustomerRepository _customerRepository;
    private readonly IProductRepository _productRepository;
    private readonly IOrderRepository _orderRepository;

    public CreateOrderHandler(
        ICustomerRepository customerRepository,
        IProductRepository productRepository,
        IOrderRepository orderRepository)
    {
        _customerRepository = customerRepository;
        _productRepository = productRepository;
        _orderRepository = orderRepository;
    }

    public async Task<CreateOrderResponse> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        // 1. Verify customer exists
        var customerExists = await _customerRepository.ExistsAsync(request.CustomerId, cancellationToken);
        if (!customerExists)
        {
            throw new NotFoundException(nameof(Customer), request.CustomerId);
        }

        // 2. Fetch requested products
        var productIds = request.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await _productRepository.GetByIdsAsync(productIds, cancellationToken);
        var productDict = products.ToDictionary(p => p.Id);

        // 3. Ensure all requested products exist
        var missingProductIds = productIds.Where(id => !productDict.ContainsKey(id)).ToList();
        if (missingProductIds.Count != 0)
        {
            throw new NotFoundException($"The following product IDs were not found: {string.Join(", ", missingProductIds)}");
        }

        // 4. Create OrderItems with snapshot of current product name and price
        var orderItems = new List<OrderItem>();
        foreach (var itemRequest in request.Items)
        {
            var product = productDict[itemRequest.ProductId];
            var orderItem = new OrderItem(product.Name, itemRequest.Quantity, product.Price);
            orderItems.Add(orderItem);
        }

        // 5. Create Order aggregate (calculates total, sets status Pending and CreatedAt)
        var order = Order.Create(request.CustomerId, orderItems);

        // 6. Save order to SQL Server
        await _orderRepository.AddAsync(order, cancellationToken);
        await _orderRepository.SaveChangesAsync(cancellationToken);

        // 7. Return response DTO
        return new CreateOrderResponse(
            order.Id,
            order.CustomerId,
            order.Total,
            order.Status.ToString(),
            order.CreatedAt
        );
    }
}
