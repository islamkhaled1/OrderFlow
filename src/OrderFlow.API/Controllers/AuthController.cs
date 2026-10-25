using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using OrderFlow.Application.DTOs;
using OrderFlow.Application.Features.Auth.Login;
using OrderFlow.Application.Features.Auth.Register;

namespace OrderFlow.API.Controllers;

/// <summary>Public registration (customer accounts only) and login. Rate limited per client IP.</summary>
[ApiController]
[AllowAnonymous]
[EnableRateLimiting("auth")]
[Route("api/auth")]
[Produces("application/json")]
public class AuthController : ControllerBase
{
    private readonly IMediator _mediator;

    public AuthController(IMediator mediator)
    {
        _mediator = mediator;
    }

    /// <summary>Registers a customer account. Administrators cannot be created through this endpoint.</summary>
    [HttpPost("register")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AuthResponse>> Register([FromBody] RegisterRequest request, CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new RegisterCommand(request.Email, request.Password, request.Name), cancellationToken);
        return StatusCode(StatusCodes.Status201Created, response);
    }

    /// <summary>Exchanges credentials for a JWT access token.</summary>
    [HttpPost("login")]
    [ProducesResponseType(typeof(AuthResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var response = await _mediator.Send(new LoginCommand(request.Email, request.Password), cancellationToken);
        return Ok(response);
    }
}
