using FluentValidation;
using MediatR;
using OrderFlow.Application.Common.Models;
using OrderFlow.Application.DTOs;
using OrderFlow.Application.Interfaces;
using OrderFlow.Domain.Enums;

namespace OrderFlow.Application.Features.Orders.GetOrders;

public record GetOrdersQuery(
    int Page = 1,
    int PageSize = Paging.DefaultPageSize,
    string? Status = null,
    DateTime? From = null,
    DateTime? To = null,
    int? CustomerId = null) : IRequest<PagedResult<OrderListItemDto>>;

public class GetOrdersQueryValidator : AbstractValidator<GetOrdersQuery>
{
    public GetOrdersQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, Paging.MaxPageSize)
            .WithMessage($"PageSize must be between 1 and {Paging.MaxPageSize}.");
        RuleFor(x => x.Status)
            .Must(s => s == null || Enum.TryParse<OrderStatus>(s, ignoreCase: true, out _))
            .WithMessage($"Status must be one of: {string.Join(", ", Enum.GetNames<OrderStatus>())}.");
        RuleFor(x => x).Must(x => !x.From.HasValue || !x.To.HasValue || x.From <= x.To)
            .WithName("From")
            .WithMessage("'From' must be earlier than or equal to 'To'.");
    }
}

public class GetOrdersHandler : IRequestHandler<GetOrdersQuery, PagedResult<OrderListItemDto>>
{
    private readonly IOrderRepository _orderRepository;
    private readonly ICurrentUser _currentUser;

    public GetOrdersHandler(IOrderRepository orderRepository, ICurrentUser currentUser)
    {
        _orderRepository = orderRepository;
        _currentUser = currentUser;
    }

    public Task<PagedResult<OrderListItemDto>> Handle(GetOrdersQuery request, CancellationToken cancellationToken)
    {
        // Customers are always scoped to their own orders; the optional customer filter is admin-only.
        var customerId = _currentUser.IsAdmin ? request.CustomerId : _currentUser.CustomerId ?? -1;

        OrderStatus? status = request.Status is null ? null : Enum.Parse<OrderStatus>(request.Status, ignoreCase: true);

        return _orderRepository.GetPagedAsync(
            new OrderListFilter(request.Page, request.PageSize, customerId, status, request.From, request.To),
            cancellationToken);
    }
}
