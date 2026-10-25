using System.Reflection;
using System.Threading.RateLimiting;
using HealthChecks.UI.Client;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OrderFlow.API.Middleware;
using OrderFlow.API.Observability;
using OrderFlow.API.Security;
using OrderFlow.Application;
using OrderFlow.Application.Interfaces;
using OrderFlow.Infrastructure;
using OrderFlow.Infrastructure.Observability;
using OrderFlow.Infrastructure.Persistence;
using OrderFlow.Infrastructure.Persistence.Seed;
using System.Text;

var builder = WebApplication.CreateBuilder(args);

// 1. Controllers
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// 2. Swagger / OpenAPI with JWT bearer support
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "OrderFlow API",
        Version = "v1",
        Description = "E-commerce order backend: Clean Architecture, CQRS, SQL Server inventory consistency, " +
                      "idempotent order creation, transactional outbox, Redis caching and OpenTelemetry."
    });

    var xmlFile = $"{Assembly.GetExecutingAssembly().GetName().Name}.xml";
    var xmlPath = Path.Combine(AppContext.BaseDirectory, xmlFile);
    if (File.Exists(xmlPath))
    {
        c.IncludeXmlComments(xmlPath);
    }

    c.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        Description = "Paste the accessToken returned by /api/auth/login."
    });
});

// 3. Application & Infrastructure layers
builder.Services.AddApplication();
builder.Services.AddInfrastructure(builder.Configuration);

// 4. Authentication / authorization (JWT bearer). The signing key must come from user-secrets or the
//    Jwt__SigningKey environment variable; the app refuses to start without a sufficiently long key.
builder.Services.AddOptions<JwtOptions>()
    .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
    .Validate(o => !string.IsNullOrWhiteSpace(o.SigningKey) && Encoding.UTF8.GetByteCount(o.SigningKey) >= JwtOptions.MinimumSigningKeyLength,
        $"Jwt:SigningKey must be configured (user-secrets or Jwt__SigningKey env var) and be at least {JwtOptions.MinimumSigningKeyLength} bytes long.")
    .ValidateOnStart();

builder.Services.AddSingleton<ITokenService, JwtTokenService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<ICurrentUser, HttpCurrentUser>();

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
    .Configure<IOptions<JwtOptions>>((bearer, jwtAccessor) =>
    {
        var jwt = jwtAccessor.Value;
        bearer.MapInboundClaims = false; // keep claim names exactly as issued ("sub", "role", "customer_id")
        bearer.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwt.Issuer,
            ValidateAudience = true,
            ValidAudience = jwt.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = OrderFlowClaims.Subject,
            RoleClaimType = OrderFlowClaims.Role
        };
    });
builder.Services.AddAuthorization();

// 5. Rate limiting for the credential endpoints (per client IP) to slow brute-force attempts
builder.Services.AddRateLimiter(limiter =>
{
    limiter.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    limiter.AddPolicy("auth", httpContext =>
    {
        var permit = httpContext.RequestServices.GetRequiredService<IConfiguration>()
            .GetValue("RateLimiting:AuthPermitPerMinute", 10);
        return RateLimitPartition.GetFixedWindowLimiter(
            httpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = permit,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            });
    });
});

// 6. Custom metrics
builder.Services.AddSingleton<OrderFlowMetrics>();
builder.Services.AddSingleton<PendingOrdersMetrics>();

// 7. Logging: include scopes so every log line carries TraceId/SpanId for log <-> trace correlation
builder.Logging.AddSimpleConsole(o =>
{
    o.IncludeScopes = true;
    o.SingleLine = true;
    o.TimestampFormat = "HH:mm:ss ";
});

// 8. OpenTelemetry
var otelResource = ResourceBuilder.CreateDefault()
    .AddService("OrderFlow");

builder.Services.AddOpenTelemetry()
    .WithTracing(tracing =>
    {
        tracing
            .SetResourceBuilder(otelResource)
            .AddSource(OrderFlowActivitySource.Name)
            .AddSource(InfrastructureTelemetry.ActivitySourceName)
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation();

        if (builder.Configuration.GetValue("OpenTelemetry:ConsoleExporter", false))
        {
            tracing.AddConsoleExporter();
        }

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
            .AddMeter(OrderFlowMetrics.MeterName) // shared by API and infrastructure counters
            .AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddPrometheusExporter();
    });

// 9. Health checks.
//    live  : the process is up (no dependencies) -> restart if this fails.
//    ready : can serve traffic -> requires SQL Server only. Redis is an optimisation, so it is reported
//            (Degraded) in /health but does NOT make the API unready.
builder.Services.AddHealthChecks()
    .AddSqlServer(
        connectionStringFactory: sp => sp.GetRequiredService<IConfiguration>().GetConnectionString("DefaultConnection")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=OrderFlowDb;Trusted_Connection=True;TrustServerCertificate=True",
        name: "sqlserver",
        failureStatus: HealthStatus.Unhealthy,
        tags: new[] { "ready", "db" })
    .AddRedis(
        connectionStringFactory: sp => sp.GetRequiredService<IConfiguration>()["Redis:ConnectionString"]
            ?? "localhost:6379,abortConnect=false",
        name: "redis",
        failureStatus: HealthStatus.Degraded,
        tags: new[] { "cache" });

var app = builder.Build();

// Eagerly resolve gauges so the observable instruments are registered
app.Services.GetRequiredService<PendingOrdersMetrics>();
app.Services.GetRequiredService<OutboxBacklogMetrics>();

// 10. Pipeline. The exception middleware is first so it also records 401/403/429 responses.
app.UseMiddleware<ExceptionHandlingMiddleware>();

app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "OrderFlow API v1");
    c.RoutePrefix = "swagger";
});

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

// 11. Operational endpoints (anonymous; restrict at the network edge in production)
app.MapPrometheusScrapingEndpoint("/metrics");

app.MapHealthChecks("/health/live", new HealthCheckOptions { Predicate = _ => false });
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = check => check.Tags.Contains("ready"),
    // Status only: no connection details or exception messages are exposed.
    ResponseWriter = (ctx, report) => ctx.Response.WriteAsync(report.Status.ToString())
});
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = (ctx, report) =>
    {
        // Redact exception details: dependency status only.
        var sanitized = new HealthReport(
            report.Entries.ToDictionary(
                e => e.Key,
                e => new HealthReportEntry(e.Value.Status, e.Value.Description, e.Value.Duration, null, e.Value.Data, e.Value.Tags)),
            report.TotalDuration);
        return UIResponseWriter.WriteHealthCheckUIResponse(ctx, sanitized);
    }
});

// 12. Database migration, deterministic seed and (optional) administrator seeding
if (app.Configuration.GetValue("Database:AutoMigrate", true))
{
    using var scope = app.Services.CreateScope();
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
        await OrderFlowDbContextSeed.SeedAdminAsync(
            context,
            services.GetRequiredService<IPasswordHasherService>(),
            app.Configuration["Seed:AdminEmail"],
            app.Configuration["Seed:AdminPassword"],
            logger);
    }
    catch (Exception ex)
    {
        // The API stays up so /health/ready can report the database problem instead of crash-looping.
        logger.LogError(ex, "Could not run automated migrations/seeding on startup. Check SQL Server connectivity; /health/ready reports database status.");
    }
}

app.Run();

// Make Program class accessible to WebApplicationFactory in integration tests
public partial class Program { }
