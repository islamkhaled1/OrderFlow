using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi;
using OrderFlow.API.Middleware;
using OrderFlow.Application;
using OrderFlow.Infrastructure;
using OrderFlow.Infrastructure.Persistence;
using OrderFlow.Infrastructure.Persistence.Seed;

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

var app = builder.Build();

// 4. Global exception handling middleware
app.UseMiddleware<ExceptionHandlingMiddleware>();

// 5. Configure Swagger UI
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "OrderFlow API v1");
    c.RoutePrefix = "swagger";
});

app.UseAuthorization();
app.MapControllers();

// 6. Database migration & deterministic seed execution
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
