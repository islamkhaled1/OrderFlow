using MediatR;
using Microsoft.AspNetCore.Mvc;
using OrderFlow.Application.DTOs;
using OrderFlow.Application.Features.Orders.CreateOrder;
using OrderFlow.Application.Features.Orders.GetOrderById;
using OrderFlow.Application.Features.Orders.GetOrders;

namespace OrderFlow.API.Controllers;

/// <summary>
/// Orders endpoint controller.
/// 
/// ARCHITECTURAL PRINCIPLE:
/// Controllers must remain thin. All business logic, validation,
/// data access, and caching decisions are encapsulated inside
/// MediatR commands, queries, and domain models.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class OrdersController : ControllerBase
{
    private readonly IMediator _mediator;

    public OrdersController(IMediator mediator)
    {
        _mediator = mediator;
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
        var command = new CreateOrderCommand(request.CustomerId, request.Items);
        var response = await _mediator.Send(command, cancellationToken);

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
        var query = new GetOrderByIdQuery(id);
        var response = await _mediator.Send(query, cancellationToken);

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
        var query = new GetOrdersQuery();
        var response = await _mediator.Send(query, cancellationToken);

        return Ok(response);
    }
}
