using FluentValidation;

namespace OrderFlow.Application.Features.Orders.CreateOrder;

public class CreateOrderCommandValidator : AbstractValidator<CreateOrderCommand>
{
    public const int MaxItems = 50;
    public const int MaxQuantityPerItem = 1000;
    public const int MaxIdempotencyKeyLength = 128;

    public CreateOrderCommandValidator()
    {
        RuleFor(x => x.Items)
            .NotNull()
            .WithMessage("Items collection cannot be null.")
            .Must(items => items != null && items.Count > 0)
            .WithMessage("An order must contain at least one item.")
            .Must(items => items == null || items.Count <= MaxItems)
            .WithMessage($"An order cannot contain more than {MaxItems} items.");

        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.ProductId)
                .GreaterThan(0)
                .WithMessage("ProductId must be greater than zero.");

            item.RuleFor(i => i.Quantity)
                .GreaterThan(0)
                .WithMessage("Quantity must be greater than zero.")
                .LessThanOrEqualTo(MaxQuantityPerItem)
                .WithMessage($"Quantity cannot exceed {MaxQuantityPerItem} per item.");
        });

        RuleFor(x => x.IdempotencyKey)
            .MaximumLength(MaxIdempotencyKeyLength)
            .WithMessage($"Idempotency-Key cannot exceed {MaxIdempotencyKeyLength} characters.")
            .Must(k => k == null || k.Trim().Length > 0)
            .WithMessage("Idempotency-Key cannot be blank.");
    }
}
