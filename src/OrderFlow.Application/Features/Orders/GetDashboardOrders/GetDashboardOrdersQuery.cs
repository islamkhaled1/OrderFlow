using FluentValidation;
using MediatR;
using OrderFlow.Application.Common.Models;
using OrderFlow.Application.DTOs;
using OrderFlow.Domain.Enums;

namespace OrderFlow.Application.Features.Orders.GetDashboardOrders;

public record GetDashboardOrdersQuery(
    int Page = 1,
    int PageSize = Paging.DefaultPageSize,
    string? Status = null) : IRequest<PagedResult<OrderDashboardDto>>;

public class GetDashboardOrdersQueryValidator : AbstractValidator<GetDashboardOrdersQuery>
{
    public GetDashboardOrdersQueryValidator()
    {
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, Paging.MaxPageSize)
            .WithMessage($"PageSize must be between 1 and {Paging.MaxPageSize}.");
        RuleFor(x => x.Status)
            .Must(s => s == null || Enum.TryParse<OrderStatus>(s, ignoreCase: true, out _))
            .WithMessage($"Status must be one of: {string.Join(", ", Enum.GetNames<OrderStatus>())}.");
    }
}
