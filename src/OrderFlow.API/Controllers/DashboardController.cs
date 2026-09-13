using MediatR;
using Microsoft.AspNetCore.Mvc;
using OrderFlow.Application.DTOs;
using OrderFlow.Application.Features.Orders.GetDashboardOrders;

namespace OrderFlow.API.Controllers;

/// <summary>
/// Dashboard endpoint controller.
/// 
/// ARCHITECTURAL PRINCIPLE:
/// This controller reads strictly from the materialized view / read model
/// table ('OrderDashboardReadModels').
/// It NEVER executes expensive runtime joins across transactional tables
/// (Orders, OrderItems, Customers), demonstrating the CQRS Read Model pattern.
/// </summary>
[ApiController]
[Route("api/[controller]")]
[Produces("application/json")]
public class DashboardController : ControllerBase
{
    private readonly IMediator _mediator;

    public DashboardController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>
    /// Retrieves aggregated dashboard order metrics from the Materialized View.
    /// </summary>
    /// <remarks>
    /// CQRS: Query.
    /// Data Source: OrderDashboardReadModels (SQL Server Read Model).
    /// NOT the transactional tables. The read model is populated and kept up-to-date
    /// asynchronously by the background worker.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A list of denormalized dashboard order records.</returns>
    [HttpGet("orders")]
    [ProducesResponseType(typeof(List<OrderDashboardDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<OrderDashboardDto>>> GetDashboardOrders(CancellationToken cancellationToken)
    {
        var query = new GetDashboardOrdersQuery();
        var response = await _mediator.Send(query, cancellationToken);

        return Ok(response);
    }
}
