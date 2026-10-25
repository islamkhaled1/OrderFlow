using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderFlow.API.Observability;
using OrderFlow.Application.Common.Models;
using OrderFlow.Application.DTOs;
using OrderFlow.Application.Features.Orders.CancelOrder;
using OrderFlow.Application.Features.Orders.CompleteOrder;
using OrderFlow.Application.Features.Orders.CreateOrder;
using OrderFlow.Application.Features.Orders.GetOrderById;
using OrderFlow.Application.Features.Orders.GetOrders;
using OrderFlow.Domain.Entities;

namespace OrderFlow.API.Controllers;

/// <summary>
/// REST API endpoints for Order operations. Controllers stay thin: validation, authorization rules,
/// business rules and data access live in MediatR handlers and the domain model.
/// </summary>
[ApiController]
[Authorize]
[Route("api/[controller]")]
[Produces("application/json")]
public class OrdersController : ControllerBase
{
    public const string IdempotencyKeyHeader = "Idempotency-Key";

    private readonly IMediator _mediator;
    private readonly ILogger<OrdersController> _logger;
    private readonly OrderFlowMetrics _metrics;

    public OrdersController(IMediator mediator, ILogger<OrdersController> logger, OrderFlowMetrics metrics)
    {
        _mediator = mediator;
        _logger = logger;
        _metrics = metrics;
    }

    /// <summary>Creates an order for the authenticated customer.</summary>
    /// <remarks>
    /// The customer comes from the access token. Prices, names and totals are computed server-side and stock
    /// is reserved atomically. Send an <c>Idempotency-Key</c> header to make retries safe: a retry with the same
    /// key and payload returns the original response (header <c>Idempotent-Replayed: true</c>); the same key
    /// with a different payload is rejected with 422.
    /// </remarks>
    [HttpPost]
    [ProducesResponseType(typeof(CreateOrderResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CreateOrderResponse>> Create(
        [FromBody] CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        using var activity = OrderFlowActivitySource.Instance.StartActivity("CreateOrder");
        activity?.SetTag("order.items_count", request.Items?.Count ?? 0);

        var idempotencyKey = Request.Headers[IdempotencyKeyHeader].FirstOrDefault();
        var result = await _mediator.Send(
            new CreateOrderCommand(request.Items ?? new List<OrderItemRequestDto>(), idempotencyKey),
            cancellationToken);

        var response = result.Response;
        activity?.SetTag("order.id", response.OrderId);
        activity?.SetTag("order.status", response.Status);

        if (result.Replayed)
        {
            _metrics.RecordIdempotentReplay();
            Response.Headers["Idempotent-Replayed"] = "true";
            _logger.LogInformation("Order {OrderId} returned from idempotency record.", response.OrderId);
        }
        else
        {
            _metrics.RecordOrderCreated();
            _logger.LogInformation("Order {OrderId} created. Total: {Total}", response.OrderId, response.Total);
        }

        return CreatedAtAction(nameof(GetById), new { id = response.OrderId }, response);
    }

    /// <summary>Retrieves an order. Customers can read only their own orders; administrators any order.</summary>
    /// <remarks>Cached in Redis under 'order:{id}'; ownership is verified on every request, independent of the cache.</remarks>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(OrderDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderDetailsDto>> GetById(int id, CancellationToken cancellationToken)
    {
        using var activity = OrderFlowActivitySource.Instance.StartActivity("GetOrderById");
        activity?.SetTag("order.id", id);

        var response = await _mediator.Send(new GetOrderByIdQuery(id), cancellationToken);
        activity?.SetTag("order.status", response.Status);
        return Ok(response);
    }

    /// <summary>Lists orders (own orders for customers, all for administrators) with paging and filters.</summary>
    /// <param name="page">1-based page number.</param>
    /// <param name="pageSize">Items per page (1-100, default 20).</param>
    /// <param name="status">Pending, Completed or Cancelled.</param>
    /// <param name="from">Created at or after (UTC).</param>
    /// <param name="to">Created at or before (UTC).</param>
    /// <param name="customerId">Administrators only: restrict to one customer.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<OrderListItemDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PagedResult<OrderListItemDto>>> GetAll(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = Paging.DefaultPageSize,
        [FromQuery] string? status = null,
        [FromQuery] DateTime? from = null,
        [FromQuery] DateTime? to = null,
        [FromQuery] int? customerId = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(
            new GetOrdersQuery(page, pageSize, status, from, to, customerId), cancellationToken);
        return Ok(response);
    }

    /// <summary>Cancels a pending order and releases its reserved stock (owner or administrator).</summary>
    [HttpPost("{id:int}/cancel")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Cancel(int id, [FromBody] CancelOrderRequest? request, CancellationToken cancellationToken)
    {
        await _mediator.Send(new CancelOrderCommand(id, request?.Reason), cancellationToken);
        _metrics.RecordOrderTransition(nameof(Domain.Enums.OrderStatus.Cancelled));
        return NoContent();
    }

    /// <summary>Marks a pending order as completed (fulfilled). Administrators only.</summary>
    [HttpPost("{id:int}/complete")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Complete(int id, CancellationToken cancellationToken)
    {
        await _mediator.Send(new CompleteOrderCommand(id), cancellationToken);
        _metrics.RecordOrderTransition(nameof(Domain.Enums.OrderStatus.Completed));
        return NoContent();
    }
}
