using FluentAssertions;
using OrderFlow.Application.DTOs;
using OrderFlow.Application.Features.Orders.CreateOrder;
using Xunit;

namespace OrderFlow.UnitTests.Application;

public class CreateOrderCommandValidatorTests
{
    private readonly CreateOrderCommandValidator _validator = new();

    [Fact]
    public void Validate_ValidCommand_PassesValidation()
    {
        // Arrange
        var command = new CreateOrderCommand(1, new List<OrderItemRequestDto>
        {
            new(1, 2),
            new(2, 1)
        });

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Validate_InvalidCustomerId_FailsValidation(int invalidCustomerId)
    {
        // Arrange
        var command = new CreateOrderCommand(invalidCustomerId, new List<OrderItemRequestDto>
        {
            new(1, 1)
        });

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "CustomerId");
    }

    [Fact]
    public void Validate_EmptyItemsList_FailsValidation()
    {
        // Arrange
        var command = new CreateOrderCommand(1, new List<OrderItemRequestDto>());

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Items");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_InvalidQuantity_FailsValidation(int invalidQuantity)
    {
        // Arrange
        var command = new CreateOrderCommand(1, new List<OrderItemRequestDto>
        {
            new(1, invalidQuantity)
        });

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName.Contains("Quantity"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_InvalidProductId_FailsValidation(int invalidProductId)
    {
        // Arrange
        var command = new CreateOrderCommand(1, new List<OrderItemRequestDto>
        {
            new(invalidProductId, 1)
        });

        // Act
        var result = _validator.Validate(command);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName.Contains("ProductId"));
    }
}
