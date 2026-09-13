using FluentAssertions;
using OrderFlow.Domain.Entities;
using OrderFlow.Domain.Exceptions;
using Xunit;

namespace OrderFlow.UnitTests.Domain;

public class OrderItemValidationTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-10)]
    public void Constructor_WithZeroOrNegativeQuantity_ThrowsDomainException(int invalidQuantity)
    {
        // Act
        var act = () => new OrderItem("Keyboard", invalidQuantity, 100m);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("*quantity must be greater than zero*");
    }

    [Fact]
    public void Constructor_WithNegativePrice_ThrowsDomainException()
    {
        // Act
        var act = () => new OrderItem("Keyboard", 2, -10m);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("*price cannot be negative*");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_WithEmptyProductName_ThrowsDomainException(string emptyName)
    {
        // Act
        var act = () => new OrderItem(emptyName, 1, 50m);

        // Assert
        act.Should().Throw<DomainException>()
            .WithMessage("*Product name snapshot cannot be empty*");
    }

    [Fact]
    public void LineTotal_CalculatesQuantityTimesUnitPrice()
    {
        // Arrange & Act
        var item = new OrderItem("Keyboard", 4, 125m);

        // Assert
        item.LineTotal.Should().Be(500m);
    }
}
