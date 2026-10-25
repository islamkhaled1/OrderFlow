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
        var command = new CreateOrderCommand(new List<OrderItemRequestDto> { new(1, 2), new(2, 1) });

        _validator.Validate(command).IsValid.Should().BeTrue();
    }

    [Fact]
    public void Validate_EmptyItemsList_FailsValidation()
    {
        var command = new CreateOrderCommand(new List<OrderItemRequestDto>());

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName == "Items");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_InvalidQuantity_FailsValidation(int invalidQuantity)
    {
        var command = new CreateOrderCommand(new List<OrderItemRequestDto> { new(1, invalidQuantity) });

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName.Contains("Quantity"));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Validate_InvalidProductId_FailsValidation(int invalidProductId)
    {
        var command = new CreateOrderCommand(new List<OrderItemRequestDto> { new(invalidProductId, 1) });

        var result = _validator.Validate(command);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.PropertyName.Contains("ProductId"));
    }

    [Fact]
    public void Validate_IdempotencyKeyTooLong_FailsValidation()
    {
        var command = new CreateOrderCommand(new List<OrderItemRequestDto> { new(1, 1) }, new string('k', 129));

        _validator.Validate(command).IsValid.Should().BeFalse();
    }
}
