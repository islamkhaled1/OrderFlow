using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using OrderFlow.Application.Common.Exceptions;
using OrderFlow.Domain.Exceptions;

namespace OrderFlow.API.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/problem+json";

        switch (exception)
        {
            case ValidationException validationException:
                _logger.LogWarning("Validation failure: {Message}", validationException.Message);
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                var validationProblemDetails = new ValidationProblemDetails(validationException.Errors)
                {
                    Status = (int)HttpStatusCode.BadRequest,
                    Title = "Validation Error",
                    Detail = "One or more validation errors occurred.",
                    Instance = context.Request.Path
                };
                await context.Response.WriteAsync(JsonSerializer.Serialize(validationProblemDetails));
                break;

            case NotFoundException notFoundException:
                _logger.LogWarning("Resource not found: {Message}", notFoundException.Message);
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                var notFoundProblemDetails = new ProblemDetails
                {
                    Status = (int)HttpStatusCode.NotFound,
                    Title = "Not Found",
                    Detail = notFoundException.Message,
                    Instance = context.Request.Path
                };
                await context.Response.WriteAsync(JsonSerializer.Serialize(notFoundProblemDetails));
                break;

            case BadRequestException badRequestException:
                _logger.LogWarning("Bad request: {Message}", badRequestException.Message);
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                var badRequestProblemDetails = new ProblemDetails
                {
                    Status = (int)HttpStatusCode.BadRequest,
                    Title = "Bad Request",
                    Detail = badRequestException.Message,
                    Instance = context.Request.Path
                };
                await context.Response.WriteAsync(JsonSerializer.Serialize(badRequestProblemDetails));
                break;

            case DomainException domainException:
                _logger.LogWarning("Domain rule violation: {Message}", domainException.Message);
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                var domainProblemDetails = new ProblemDetails
                {
                    Status = (int)HttpStatusCode.BadRequest,
                    Title = "Domain Rule Violation",
                    Detail = domainException.Message,
                    Instance = context.Request.Path
                };
                await context.Response.WriteAsync(JsonSerializer.Serialize(domainProblemDetails));
                break;

            default:
                _logger.LogError(exception, "An unhandled error occurred while processing the request.");
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                var internalServerErrorDetails = new ProblemDetails
                {
                    Status = (int)HttpStatusCode.InternalServerError,
                    Title = "Internal Server Error",
                    Detail = "An unexpected error occurred. Please try again later.",
                    Instance = context.Request.Path
                };
                await context.Response.WriteAsync(JsonSerializer.Serialize(internalServerErrorDetails));
                break;
        }
    }
}
