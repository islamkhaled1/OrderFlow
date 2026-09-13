using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using OrderFlow.Application.Interfaces;
using OrderFlow.Domain.Entities;
using OrderFlow.Domain.Enums;
using OrderFlow.Infrastructure.Services;
using Xunit;

namespace OrderFlow.UnitTests.Application;

public class OrderProcessingServiceTests
{
    private readonly Mock<IOrderRepository> _orderRepoMock = new();
    private readonly Mock<ICacheService> _cacheServiceMock = new();
    private readonly Mock<ILogger<OrderProcessingService>> _loggerMock = new();

    [Fact]
    public async Task ProcessPendingOrdersAsync_TransitionsOrdersAndInvalidatesCache()
    {
        // Arrange
        var order1 = Order.Create(1, new List<OrderItem> { new("Mouse", 1, 50m) });
        typeof(Order).GetProperty(nameof(Order.Id))!.SetValue(order1, 101);

        var order2 = Order.Create(2, new List<OrderItem> { new("Keyboard", 1, 120m) });
        typeof(Order).GetProperty(nameof(Order.Id))!.SetValue(order2, 102);

        var pendingOrders = new List<Order> { order1, order2 };

        _orderRepoMock
            .Setup(r => r.GetPendingOrdersAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(pendingOrders);

        var service = new OrderProcessingService(
            _orderRepoMock.Object,
            _cacheServiceMock.Object,
            _loggerMock.Object);

        // Act
        var processedIds = await service.ProcessPendingOrdersAsync(CancellationToken.None);

        // Assert
        processedIds.Should().BeEquivalentTo(new[] { 101, 102 });
        order1.Status.Should().Be(OrderStatus.Completed);
        order2.Status.Should().Be(OrderStatus.Completed);

        _orderRepoMock.Verify(r => r.SaveChangesAsync(It.IsAny<CancellationToken>()), Times.Once);

        _cacheServiceMock.Verify(c => c.RemoveAsync("order:101", It.IsAny<CancellationToken>()), Times.Once);
        _cacheServiceMock.Verify(c => c.RemoveAsync("order:102", It.IsAny<CancellationToken>()), Times.Once);
    }
}
