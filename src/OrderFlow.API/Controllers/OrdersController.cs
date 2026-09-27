using MediatR;
using Microsoft.AspNetCore.Mvc;
using OrderFlow.API.Observability;
using OrderFlow.Application.DTOs;
using OrderFlow.Application.Features.Orders.CreateOrder;
using OrderFlow.Application.Features.Orders.GetOrderById;
using OrderFlow.Application.Features.Orders.GetOrders;

namespace OrderFlow.API.Controllers;

/// <summary>
/// REST API endpoints for Order operations.
/// The controller is deliberately thin: validation, business rules,
/// data access, and caching decisions are encapsulated inside
/// MediatR commands, queries, and domain models.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class OrdersController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly ILogger<OrdersController> _logger;
    private readonly OrderFlowMetrics _metrics;

    public OrdersController(IMediator mediator, ILogger<OrdersController> logger, OrderFlowMetrics metrics)
    {
        _mediator = mediator;
        _logger = logger;
        _metrics = metrics;
    }

    /// <summary>
    /// Creates a new order.
    /// </summary>
    /// <remarks>
    /// CQRS: Command.
    /// Modifies application state, validates requested products against SQL Server,
    /// snapshots product names and unit prices, calculates total server-side,
    /// and initializes the order with 'Pending' status.
    /// </remarks>
    /// <param name="request">The order creation request containing CustomerId and item quantities.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The created order details.</returns>
    [HttpPost]
    [ProducesResponseType(typeof(CreateOrderResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CreateOrderResponse>> Create(
        [FromBody] CreateOrderRequest request,
        CancellationToken cancellationToken)
    {
        using var activity = OrderFlowActivitySource.Instance.StartActivity("CreateOrder");
        activity?.SetTag("order.customer_id", request.CustomerId);
        activity?.SetTag("order.items_count", request.Items.Count);

        _logger.LogInformation("Creating order for CustomerId {CustomerId} with {ItemCount} items",
            request.CustomerId, request.Items.Count);

        var command = new CreateOrderCommand(request.CustomerId, request.Items);
        var response = await _mediator.Send(command, cancellationToken);

        activity?.SetTag("order.id", response.OrderId);
        activity?.SetTag("order.total", response.Total);
        activity?.SetTag("order.status", response.Status);

        _metrics.RecordOrderCreated();

        _logger.LogInformation("Order {OrderId} created successfully. Total: {Total}, Status: {Status}",
            response.OrderId, response.Total, response.Status);

        return CreatedAtAction(nameof(GetById), new { id = response.OrderId }, response);
    }

    /// <summary>
    /// Retrieves full order details by order ID.
    /// </summary>
    /// <remarks>
    /// CQRS: Query.
    /// Frequently requested order details are cached in Redis under key 'order:{id}'.
    /// If cached, the DTO is deserialized and returned immediately.
    /// If not cached (or if Redis is temporarily unreachable), the query falls back
    /// to SQL Server, builds the DTO, caches it with an expiration TTL, and returns it.
    /// </remarks>
    /// <param name="id">The unique identifier of the order.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The detailed order response.</returns>
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(OrderDetailsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderDetailsDto>> GetById(
        int id,
        CancellationToken cancellationToken)
    {
        using var activity = OrderFlowActivitySource.Instance.StartActivity("GetOrderById");
        activity?.SetTag("order.id", id);

        _logger.LogInformation("Retrieving order {OrderId}", id);

        var query = new GetOrderByIdQuery(id);
        var response = await _mediator.Send(query, cancellationToken);

        activity?.SetTag("order.status", response.Status);

        _logger.LogInformation("Order {OrderId} retrieved successfully. Status: {Status}",
            id, response.Status);

        return Ok(response);
    }

    /// <summary>
    /// Lists all orders.
    /// </summary>
    /// <remarks>
    /// CQRS: Query.
    /// Performs a fast, read-only query using AsNoTracking() against SQL Server.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of order summary items.</returns>
    [HttpGet]
    [ProducesResponseType(typeof(List<OrderListItemDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<OrderListItemDto>>> GetAll(CancellationToken cancellationToken)
    {
        using var activity = OrderFlowActivitySource.Instance.StartActivity("GetOrders");

        _logger.LogInformation("Retrieving all orders");

        var query = new GetOrdersQuery();
        var response = await _mediator.Send(query, cancellationToken);

        activity?.SetTag("orders.count", response.Count);

        _logger.LogInformation("Retrieved {OrderCount} orders", response.Count);

        return Ok(response);
    }
}
