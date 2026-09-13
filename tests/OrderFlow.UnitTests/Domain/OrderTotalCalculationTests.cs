using FluentAssertions;
using OrderFlow.Domain.Entities;
using Xunit;

namespace OrderFlow.UnitTests.Domain;

public class OrderTotalCalculationTests
{
    [Fact]
    public void CreateOrder_WithMultipleItems_CalculatesCorrectTotal()
    {
        // Arrange
        var customerId = 1;
        var items = new List<OrderItem>
        {
            new("Keyboard", 2, 500m), // 1000
            new("Mouse", 1, 300m)     // 300
        };

        // Act
        var order = Order.Create(customerId, items);

        // Assert
        order.Total.Should().Be(1300m);
    }

    [Fact]
    public void CreateOrder_WithSingleItem_CalculatesCorrectTotal()
    {
        // Arrange
        var customerId = 1;
        var items = new List<OrderItem>
        {
            new("4K Monitor", 3, 450m) // 1350
        };

        // Act
        var order = Order.Create(customerId, items);

        // Assert
        order.Total.Should().Be(1350m);
    }
}
