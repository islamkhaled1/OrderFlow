using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using OrderFlow.Application.Interfaces;
using OrderFlow.Infrastructure.BackgroundServices;
using OrderFlow.Infrastructure.Persistence;
using OrderFlow.IntegrationTests.Infrastructure;

namespace OrderFlow.IntegrationTests;

/// <summary>
/// Boots the real API against a REAL SQL Server database created from the EF migrations (so migrations,
/// constraints, transactions and locking are all exercised). Each factory gets its own database, dropped on dispose.
///
/// SQL Server comes from the ORDERFLOW_TEST_SQL environment variable (a server-level connection string
/// WITHOUT Initial Catalog). Default: SQL Server LocalDB. CI sets it to a SQL Server service container.
/// Redis is replaced by <see cref="TestMemoryCacheService"/> (outages are simulated through its flag).
/// Background workers are removed; tests drive the outbox/maintenance services explicitly for determinism.
/// </summary>
public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    public const string AdminEmail = "admin@orderflow.test";
    public const string AdminPassword = "Adm1n-Test-Password!";
    public const string TestSigningKey = "integration-tests-only-signing-key-0123456789";

    private static readonly string ServerConnection =
        Environment.GetEnvironmentVariable("ORDERFLOW_TEST_SQL")
        ?? "Server=(localdb)\\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=True";

    private readonly string _databaseName = "OrderFlowTests_" + Guid.NewGuid().ToString("N");

    public string ConnectionString => $"{ServerConnection};Database={_databaseName};MultipleActiveResultSets=true";

    public TestMemoryCacheService Cache { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("Jwt:SigningKey", TestSigningKey);
        builder.UseSetting("Seed:AdminEmail", AdminEmail);
        builder.UseSetting("Seed:AdminPassword", AdminPassword);
        builder.UseSetting("RateLimiting:AuthPermitPerMinute", "100000");
        builder.UseSetting("ConnectionStrings:DefaultConnection", ConnectionString);

        builder.ConfigureServices(services =>
        {
            // Workers off: tests call OutboxProcessor / maintenance services directly.
            foreach (var d in services.Where(d =>
                         d.ImplementationType == typeof(OutboxProcessorBackgroundService) ||
                         d.ImplementationType == typeof(MaintenanceBackgroundService)).ToList())
            {
                services.Remove(d);
            }

            // Point the DbContext at this factory's database (Infrastructure read the connection string eagerly).
            services.RemoveAll<DbContextOptions<OrderFlowDbContext>>();
            services.RemoveAll<DbContextOptions>();
            services.AddDbContext<OrderFlowDbContext>(options =>
                options.UseSqlServer(ConnectionString, sql => sql.EnableRetryOnFailure(3, TimeSpan.FromSeconds(2), null)));

            services.RemoveAll<ICacheService>();
            services.AddSingleton<ICacheService>(Cache);
        });
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            try
            {
                using var scope = Services.CreateScope();
                scope.ServiceProvider.GetRequiredService<OrderFlowDbContext>().Database.EnsureDeleted();
            }
            catch
            {
                // best-effort cleanup of the throw-away test database
            }
        }

        base.Dispose(disposing);
    }
}
