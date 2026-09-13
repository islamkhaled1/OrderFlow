using FluentAssertions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using OrderFlow.Application.Common.Exceptions;
using OrderFlow.Application.Common.Options;
using OrderFlow.Application.DTOs;
using OrderFlow.Application.Features.Orders.GetOrderById;
using OrderFlow.Application.Interfaces;
using OrderFlow.Domain.Entities;
using Xunit;

namespace OrderFlow.UnitTests.Application;

public class GetOrderByIdHandlerTests
{
    private readonly Mock<IOrderRepository> _orderRepoMock = new();
    private readonly Mock<ICacheService> _cacheServiceMock = new();
    private readonly Mock<ILogger<GetOrderByIdHandler>> _loggerMock = new();
    private readonly IOptions<CachingOptions> _cachingOptions = Options.Create(new CachingOptions { OrderDetailsExpirationMinutes = 10 });

    private GetOrderByIdHandler CreateHandler() =>
        new(_orderRepoMock.Object, _cacheServiceMock.Object, _cachingOptions, _loggerMock.Object);

    [Fact]
    public async Task Handle_WhenCacheHit_ReturnsCachedDtoWithoutCallingRepository()
    {
        // Arrange
        var cachedDto = new OrderDetailsDto(
            OrderId: 42,
            CustomerId: 1,
            CustomerName: "John Doe",
            CreatedAt: DateTime.UtcNow,
            Status: "Pending",
            Total: 100m,
            Items: new List<OrderItemDto> { new("Mouse", 1, 100m, 100m) }
        );

        _cacheServiceMock
            .Setup(c => c.GetAsync<OrderDetailsDto>("order:42", It.IsAny<CancellationToken>()))
            .ReturnsAsync(cachedDto);

        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(new GetOrderByIdQuery(42), CancellationToken.None);

        // Assert
        result.Should().BeEquivalentTo(cachedDto);
        _orderRepoMock.Verify(r => r.GetByIdWithDetailsAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Handle_WhenCacheMiss_QueriesRepositoryAndCachesResult()
    {
        // Arrange
        _cacheServiceMock
            .Setup(c => c.GetAsync<OrderDetailsDto>("order:10", It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrderDetailsDto?)null);

        var order = Order.Create(1, new List<OrderItem> { new("Keyboard", 1, 150m) });
        typeof(Order).GetProperty(nameof(Order.Id))!.SetValue(order, 10);
        order.Customer = new Customer(1, "Alice Johnson");

        _orderRepoMock
            .Setup(r => r.GetByIdWithDetailsAsync(10, It.IsAny<CancellationToken>()))
            .ReturnsAsync(order);

        var handler = CreateHandler();

        // Act
        var result = await handler.Handle(new GetOrderByIdQuery(10), CancellationToken.None);

        // Assert
        result.Should().NotBeNull();
        result.OrderId.Should().Be(10);
        result.CustomerName.Should().Be("Alice Johnson");
        result.Total.Should().Be(150m);

        _cacheServiceMock.Verify(c => c.SetAsync(
            "order:10",
            It.Is<OrderDetailsDto>(d => d.OrderId == 10 && d.Total == 150m),
            TimeSpan.FromMinutes(10),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Handle_WhenOrderDoesNotExist_ThrowsNotFoundException()
    {
        // Arrange
        _cacheServiceMock
            .Setup(c => c.GetAsync<OrderDetailsDto>("order:999", It.IsAny<CancellationToken>()))
            .ReturnsAsync((OrderDetailsDto?)null);

        _orderRepoMock
            .Setup(r => r.GetByIdWithDetailsAsync(999, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Order?)null);

        var handler = CreateHandler();

        // Act
        var act = () => handler.Handle(new GetOrderByIdQuery(999), CancellationToken.None);

        // Assert
        await act.Should().ThrowAsync<NotFoundException>();
    }
}
