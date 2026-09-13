using FluentAssertions;
using OrderFlow.Domain.Entities;
using OrderFlow.Domain.Enums;
using OrderFlow.Domain.Exceptions;
using Xunit;

namespace OrderFlow.UnitTests.Domain;

public class OrderBusinessRulesTests
{
    [Fact]
    public void CreateOrder_WithoutItems_ThrowsDomainException()
    {
        // Arrange
        var customerId = 1;
        var emptyItems = new List<OrderItem>();

        // Act
        var act = () => Order.Create(customerId, emptyItems);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("*at least one order item*");
    }

    [Fact]
    public void CreateOrder_WithInvalidCustomerId_ThrowsDomainException()
    {
        // Arrange
        var customerId = 0;
        var items = new List<OrderItem> { new("Mouse", 1, 50m) };

        // Act
        var act = () => Order.Create(customerId, items);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("*Customer ID must be greater than zero*");
    }

    [Fact]
    public void CreateOrder_InitialStatus_IsPending()
    {
        // Arrange
        var items = new List<OrderItem> { new("Mouse", 1, 50m) };

        // Act
        var order = Order.Create(1, items);

        // Assert
        order.Status.Should().Be(OrderStatus.Pending);
        order.CreatedAt.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void MarkAsCompleted_FromPending_TransitionsToCompleted()
    {
        // Arrange
        var items = new List<OrderItem> { new("Mouse", 1, 50m) };
        var order = Order.Create(1, items);

        // Act
        order.MarkAsCompleted();

        // Assert
        order.Status.Should().Be(OrderStatus.Completed);
    }

    [Fact]
    public void MarkAsCompleted_WhenAlreadyCompleted_ThrowsDomainException()
    {
        // Arrange
        var items = new List<OrderItem> { new("Mouse", 1, 50m) };
        var order = Order.Create(1, items);
        order.MarkAsCompleted();

        // Act
        var act = () => order.MarkAsCompleted();

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("*already 'Completed'*");
    }
}
