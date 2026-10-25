using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OrderFlow.Application.Interfaces;
using OrderFlow.Domain.Entities;

namespace OrderFlow.API.Controllers;

/// <summary>Operational endpoints for administrators: outbox dead letters and read-model rebuild.</summary>
[ApiController]
[Authorize(Roles = Roles.Admin)]
[Route("api/admin")]
[Produces("application/json")]
public class AdminController : ControllerBase
{
    private readonly IOutboxAdminService _outbox;
    private readonly IOrderDashboardRefreshService _dashboard;

    public AdminController(IOutboxAdminService outbox, IOrderDashboardRefreshService dashboard)
    {
        _outbox = outbox;
        _dashboard = dashboard;
    }

    /// <summary>Lists outbox messages that exhausted their retries, with the last error, for investigation.</summary>
    [HttpGet("outbox/dead-letters")]
    [ProducesResponseType(typeof(IReadOnlyList<DeadLetterDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<DeadLetterDto>>> GetDeadLetters(
        [FromQuery] int take = 50, CancellationToken cancellationToken = default)
        => Ok(await _outbox.GetDeadLettersAsync(take, cancellationToken));

    /// <summary>Re-queues a dead-lettered message (after fixing the underlying problem).</summary>
    [HttpPost("outbox/dead-letters/{id:guid}/requeue")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Requeue(Guid id, CancellationToken cancellationToken)
        => await _outbox.RequeueAsync(id, cancellationToken) ? NoContent() : NotFound();

    /// <summary>Rebuilds the dashboard read model from the transactional tables (reconciliation).</summary>
    [HttpPost("dashboard/rebuild")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> RebuildDashboard(CancellationToken cancellationToken)
    {
        await _dashboard.RefreshDashboardAsync(cancellationToken);
        return NoContent();
    }
}
