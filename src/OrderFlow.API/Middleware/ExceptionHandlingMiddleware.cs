using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using OrderFlow.API.Observability;
using OrderFlow.Application.Common.Exceptions;
using OrderFlow.Domain.Exceptions;

namespace OrderFlow.API.Middleware;

/// <summary>Maps application/domain exceptions to RFC 7807 problem responses and records request metrics.</summary>
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
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // Client disconnected: not a server error.
            context.Response.StatusCode = 499;
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
        finally
        {
            var route = GetRoute(context);
            _metrics.RecordHttpRequest(context.Request.Method, route, context.Response.StatusCode);
            if (context.Response.StatusCode >= 400 && !context.Items.ContainsKey(ErrorRecordedKey))
            {
                // 401/403 produced by the authentication middleware never throw; count them too.
                _metrics.RecordHttpError(context.Response.StatusCode, context.Request.Method, route);
            }
        }
    }

    private const string ErrorRecordedKey = "orderflow.error-recorded";

    private static string GetRoute(HttpContext context)
    {
        if (context.GetEndpoint() is RouteEndpoint routeEndpoint &&
            !string.IsNullOrEmpty(routeEndpoint.RoutePattern.RawText))
        {
            var raw = routeEndpoint.RoutePattern.RawText;
            return raw.StartsWith('/') ? raw : "/" + raw;
        }

        // Unmatched paths collapse to one label to keep metric cardinality bounded.
        return context.GetEndpoint() is null ? "unmatched" : context.Request.Path.Value ?? "/";
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var (status, title, detail, extensions) = Map(exception);

        if (status >= 500)
            _logger.LogError(exception, "An unhandled error occurred while processing the request.");
        else
            _logger.LogWarning("{Title}: {Message}", title, exception.Message);

        context.Items[ErrorRecordedKey] = true;
        _metrics.RecordHttpError(status, context.Request.Method, GetRoute(context));

        context.Response.StatusCode = status;
        context.Response.ContentType = "application/problem+json";

        ProblemDetails problem = exception is ValidationException validation
            ? new ValidationProblemDetails(validation.Errors)
            : new ProblemDetails();

        problem.Status = status;
        problem.Title = title;
        problem.Detail = detail;
        problem.Instance = context.Request.Path;
        foreach (var (key, value) in extensions)
            problem.Extensions[key] = value;

        await context.Response.WriteAsync(JsonSerializer.Serialize(problem, problem.GetType()));
    }

    private static (int Status, string Title, string Detail, Dictionary<string, object?> Extensions) Map(Exception exception)
    {
        var none = new Dictionary<string, object?>();
        return exception switch
        {
            ValidationException => ((int)HttpStatusCode.BadRequest, "Validation Error", "One or more validation errors occurred.", none),
            NotFoundException e => ((int)HttpStatusCode.NotFound, "Not Found", e.Message, none),
            BadRequestException e => ((int)HttpStatusCode.BadRequest, "Bad Request", e.Message, none),
            UnauthorizedException e => ((int)HttpStatusCode.Unauthorized, "Unauthorized", e.Message, none),
            ForbiddenException e => ((int)HttpStatusCode.Forbidden, "Forbidden", e.Message, none),
            IdempotencyKeyReuseException e => (422, "Idempotency Key Reuse", e.Message, none),
            InsufficientStockException e => ((int)HttpStatusCode.Conflict, "Insufficient Stock", e.Message,
                new Dictionary<string, object?> { ["shortages"] = e.Shortages }),
            ConflictException e => ((int)HttpStatusCode.Conflict, "Conflict", e.Message, none),
            InvalidOrderStateTransitionException e => ((int)HttpStatusCode.Conflict, "Invalid State Transition", e.Message, none),
            DomainException e => ((int)HttpStatusCode.BadRequest, "Domain Rule Violation", e.Message, none),
            _ => ((int)HttpStatusCode.InternalServerError, "Internal Server Error",
                "An unexpected error occurred. Please try again later.", none)
        };
    }
}
