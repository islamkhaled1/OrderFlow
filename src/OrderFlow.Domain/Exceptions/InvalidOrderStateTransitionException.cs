using OrderFlow.Domain.Enums;

namespace OrderFlow.Domain.Exceptions;

/// <summary>Raised when an order is asked to move to a state that is not reachable from its current state.</summary>
public class InvalidOrderStateTransitionException : DomainException
{
    public OrderStatus From { get; }
    public OrderStatus To { get; }

    public InvalidOrderStateTransitionException(int orderId, OrderStatus from, OrderStatus to)
        : base($"Order {orderId} is already '{from}' and cannot be transitioned to '{to}'.")
    {
        From = from;
        To = to;
    }
}
