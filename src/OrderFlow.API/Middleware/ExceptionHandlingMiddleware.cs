using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using OrderFlow.API.Observability;
using OrderFlow.Application.Common.Exceptions;
using OrderFlow.Domain.Exceptions;

namespace OrderFlow.API.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    private readonly OrderFlowMetrics _metrics;

    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger, OrderFlowMetrics metrics)
    {
        _next = next;
        _logger = logger;
        _metrics = metrics;
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
        finally
        {
            var route = GetRoute(context);
            _metrics.RecordHttpRequest(context.Request.Method, route, context.Response.StatusCode);
        }
    }

    private static string GetRoute(HttpContext context)
    {
        if (context.GetEndpoint() is RouteEndpoint routeEndpoint &&
            !string.IsNullOrEmpty(routeEndpoint.RoutePattern.RawText))
        {
            var raw = routeEndpoint.RoutePattern.RawText;
            return raw.StartsWith('/') ? raw : "/" + raw;
        }

        return context.Request.Path.Value ?? "/";
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/problem+json";
        var method = context.Request.Method;
        var route = GetRoute(context);

        switch (exception)
        {
            case ValidationException validationException:
                _logger.LogWarning("Validation failure: {Message}", validationException.Message);
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                _metrics.RecordHttpError(context.Response.StatusCode, method, route);
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
                _metrics.RecordHttpError(context.Response.StatusCode, method, route);
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
                _metrics.RecordHttpError(context.Response.StatusCode, method, route);
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
                _metrics.RecordHttpError(context.Response.StatusCode, method, route);
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
                _metrics.RecordHttpError(context.Response.StatusCode, method, route);
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
