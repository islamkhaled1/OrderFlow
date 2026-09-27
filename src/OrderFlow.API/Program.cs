using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OrderFlow.API.Middleware;
using OrderFlow.API.Observability;
using OrderFlow.Application;
using OrderFlow.Infrastructure;
using OrderFlow.Infrastructure.Persistence;
using OrderFlow.Infrastructure.Persistence.Seed;
using HealthChecks.UI.Client;

var builder = WebApplication.CreateBuilder(args);

// 1. Add API controllers
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// 2. Configure Swagger / OpenAPI documentation
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "OrderFlow API",
        Version = "v1",
        Description = "Educational E-Commerce Backend demonstrating Clean Architecture, CQRS, Redis Caching, Materialized Views, and Background Processing in .NET 10."
    });

    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }
});

// 3. Register Application & Infrastructure Clean Architecture layers
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// 4. Register custom metrics
builder.Services.AddSingleton<OrderFlowMetrics>();
builder.Services.AddSingleton<PendingOrdersMetrics>();

// 5. Configure OpenTelemetry
var otelResource = ResourceBuilder.CreateDefault()
    .AddService("OrderFlow");

// 5a. Tracing
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .SetResourceBuilder(otelResource)
            .AddSource(OrderFlowActivitySource.Name)
            .AddAspNetCoreInstrumentation()
            .AddConsoleExporter();

        // Export to OTLP (Jaeger) if configured
        var otlpEndpoint = builder.Configuration["OpenTelemetry:OtlpEndpoint"];
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            tracing.AddOtlpExporter(opts => opts.Endpoint = new Uri(otlpEndpoint));
        }
    })
    .WithMetrics(metrics =>
    {
        metrics
            .SetResourceBuilder(otelResource)
            .AddMeter(OrderFlowMetrics.MeterName)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddPrometheusExporter();
    });

// 6. Configure Health Checks
var sqlConnectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Server=(localdb)\\MSSQLLocalDB;Database=OrderFlowDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";

var redisConnectionString = builder.Configuration["Redis:ConnectionString"]
    ?? "localhost:6379,abortConnect=false";

builder.Services.AddHealthChecks()
    .AddSqlServer(
        connectionString: sqlConnectionString,
        name: "sqlserver",
        tags: new[] { "db", "sql" })
    .AddRedis(
        redisConnectionString: redisConnectionString,
        name: "redis",
        tags: new[] { "cache", "redis" });

var app = builder.Build();

// Eagerly resolve PendingOrdersMetrics so the observable gauge is registered
app.Services.GetRequiredService<PendingOrdersMetrics>();

// 7. Global exception handling middleware
app.UseMiddleware<ExceptionHandlingMiddleware>();

// 8. Configure Swagger UI
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "OrderFlow API v1");
    c.RoutePrefix = "swagger";
});

app.UseAuthorization();
app.MapControllers();

// 9. Map Prometheus metrics endpoint
app.MapPrometheusScrapingEndpoint("/metrics");

// 10. Map health check endpoint
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = UIResponseWriter.WriteHealthCheckUIResponse
});

// 11. Database migration & deterministic seed execution
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    var context = services.GetRequiredService<OrderFlowDbContext>();

    try
    {
        if (context.Database.IsSqlServer())
        {
            await context.Database.MigrateAsync();
        }
        await OrderFlowDbContextSeed.SeedAsync(context, logger);
    }
    catch (Exception ex)
    {
        logger.LogWarning(ex, "Could not run automated migrations/seeding on startup. Ensure SQL Server is accessible when running database operations.");
    }
}

app.Run();

// Make Program class accessible to WebApplicationFactory in integration tests
public partial class Program { }
