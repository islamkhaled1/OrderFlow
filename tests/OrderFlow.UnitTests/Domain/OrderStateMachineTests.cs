using FluentAssertions;
using OrderFlow.Domain.Entities;
using OrderFlow.Domain.Enums;
using OrderFlow.Domain.Exceptions;
using Xunit;

namespace OrderFlow.UnitTests.Domain;

public class OrderStateMachineTests
{
    private static Order NewPendingOrder() =>
        Order.Create(1, new[] { new OrderItem(10, "Keyboard", 2, 50m) });

    [Theory]
    [InlineData(OrderStatus.Pending, OrderStatus.Completed, true)]
    [InlineData(OrderStatus.Pending, OrderStatus.Cancelled, true)]
    [InlineData(OrderStatus.Pending, OrderStatus.Pending, false)]
    [InlineData(OrderStatus.Completed, OrderStatus.Pending, false)]
    [InlineData(OrderStatus.Completed, OrderStatus.Cancelled, false)]
    [InlineData(OrderStatus.Completed, OrderStatus.Completed, false)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Pending, false)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Completed, false)]
    [InlineData(OrderStatus.Cancelled, OrderStatus.Cancelled, false)]
    public void CanTransition_only_allows_leaving_Pending(OrderStatus from, OrderStatus to, bool expected)
    {
        Order.CanTransition(from, to).Should().Be(expected);
    }

    [Fact]
    public void New_order_is_pending_with_no_terminal_timestamps()
    {
        var order = NewPendingOrder();

        order.Status.Should().Be(OrderStatus.Pending);
        order.CompletedAt.Should().BeNull();
        order.CancelledAt.Should().BeNull();
        order.Total.Should().Be(100m);
    }

    [Fact]
    public void Cancel_moves_to_Cancelled_and_records_reason_and_time()
    {
        var order = NewPendingOrder();

        order.Cancel("  changed my mind ");

        order.Status.Should().Be(OrderStatus.Cancelled);
        order.CancellationReason.Should().Be("changed my mind");
        order.CancelledAt.Should().NotBeNull();
        order.CompletedAt.Should().BeNull();
    }

    [Fact]
    public void MarkAsCompleted_moves_to_Completed_and_records_time()
    {
        var order = NewPendingOrder();

        order.MarkAsCompleted();

        order.Status.Should().Be(OrderStatus.Completed);
        order.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public void Cancelling_twice_is_rejected_so_stock_can_only_be_released_once()
    {
        var order = NewPendingOrder();
        order.Cancel("first");

        var act = () => order.Cancel("second");

        act.Should().Throw<InvalidOrderStateTransitionException>()
            .Which.From.Should().Be(OrderStatus.Cancelled);
        order.CancellationReason.Should().Be("first");
    }

    [Fact]
    public void Completed_order_cannot_be_cancelled()
    {
        var order = NewPendingOrder();
        order.MarkAsCompleted();

        var act = () => order.Cancel("too late");

        act.Should().Throw<InvalidOrderStateTransitionException>();
        order.Status.Should().Be(OrderStatus.Completed);
        order.CancelledAt.Should().BeNull();
    }

    [Fact]
    public void Cancelled_order_cannot_be_completed()
    {
        var order = NewPendingOrder();
        order.Cancel("no");

        var act = () => order.MarkAsCompleted();

        act.Should().Throw<InvalidOrderStateTransitionException>()
            .Which.To.Should().Be(OrderStatus.Completed);
        order.CompletedAt.Should().BeNull();
    }

    [Fact]
    public void Completing_twice_is_rejected()
    {
        var order = NewPendingOrder();
        order.MarkAsCompleted();

        var act = () => order.MarkAsCompleted();

        act.Should().Throw<InvalidOrderStateTransitionException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Cancel_requires_a_reason_and_leaves_the_order_pending(string reason)
    {
        var order = NewPendingOrder();

        var act = () => order.Cancel(reason);

        act.Should().Throw<DomainException>();
        order.Status.Should().Be(OrderStatus.Pending);
    }
}
