using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderFlow.Application.Common.Models;
using OrderFlow.Application.DTOs;
using OrderFlow.Application.Features.Orders.GetDashboardOrders;
using OrderFlow.Domain.Entities;

namespace OrderFlow.API.Controllers;

/// <summary>
/// Dashboard endpoint (administrators only). Reads strictly from the 'OrderDashboardReadModels'
/// table and never joins the transactional tables. The read model is eventually consistent: it is updated
/// by the outbox processor shortly after each order change.
/// </summary>
[ApiController]
[Authorize(Roles = Roles.Admin)]
[Route("api/[controller]")]
[Produces("application/json")]
public class DashboardController : ControllerBase
{
    private readonly IMediator _mediator;

    public DashboardController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Paged dashboard order rows from the read model.</summary>
    [HttpGet("orders")]
    [ProducesResponseType(typeof(PagedResult<OrderDashboardDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<OrderDashboardDto>>> GetDashboardOrders(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = Paging.DefaultPageSize,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        var response = await _mediator.Send(new GetDashboardOrdersQuery(page, pageSize, status), cancellationToken);
        return Ok(response);
    }
}
