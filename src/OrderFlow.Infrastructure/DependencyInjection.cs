using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderFlow.Application.Common.Options;
using OrderFlow.Application.Interfaces;
using OrderFlow.Infrastructure.BackgroundServices;
using OrderFlow.Infrastructure.Caching;
using OrderFlow.Infrastructure.Idempotency;
using OrderFlow.Infrastructure.Observability;
using OrderFlow.Infrastructure.Outbox;
using OrderFlow.Infrastructure.Persistence;
using OrderFlow.Infrastructure.Repositories;
using OrderFlow.Infrastructure.Security;
using OrderFlow.Infrastructure.Services;
using StackExchange.Redis;

namespace OrderFlow.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // 1. Options
        services.Configure<CachingOptions>(configuration.GetSection(CachingOptions.SectionName));
        services.Configure<RedisOptions>(configuration.GetSection(RedisOptions.SectionName));
        services.Configure<BackgroundJobOptions>(configuration.GetSection(BackgroundJobOptions.SectionName));
        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.SectionName));
        services.Configure<IdempotencyOptions>(configuration.GetSection(IdempotencyOptions.SectionName));

        // 2. SQL Server DbContext (transient-failure retries; explicit transactions go through EfUnitOfWork,
        //    which wraps them in the execution strategy)
        var connectionString = configuration.GetConnectionString("DefaultConnection")
            ?? "Server=(localdb)\\MSSQLLocalDB;Database=OrderFlowDb;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";

        services.AddDbContext<OrderFlowDbContext>(options =>
        {
            options.UseSqlServer(connectionString, sqlOptions =>
            {
                sqlOptions.MigrationsAssembly(typeof(OrderFlowDbContext).Assembly.FullName);
                sqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(5),
                    errorNumbersToAdd: null);
            });
        });

        // 3. Metrics + Redis cache with bounded, observable degradation
        services.AddSingleton<InfrastructureMetrics>();
        services.AddSingleton<OutboxBacklogMetrics>();

        services.AddSingleton<ICacheService>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<RedisCacheService>>();
            var metrics = sp.GetRequiredService<InfrastructureMetrics>();
            var redisOptions = sp.GetRequiredService<IOptions<RedisOptions>>().Value;
            IConnectionMultiplexer? multiplexer = null;

            try
            {
                var connStr = string.IsNullOrWhiteSpace(redisOptions.ConnectionString)
                    ? "localhost:6379,abortConnect=false"
                    : redisOptions.ConnectionString;

                var config = ConfigurationOptions.Parse(connStr);
                config.AbortOnConnectFail = false;
                config.ConnectTimeout = 3000;
                // A dead Redis must not stall requests: every command is bounded to half a second.
                config.AsyncTimeout = 500;
                config.SyncTimeout = 500;

                multiplexer = ConnectionMultiplexer.Connect(config);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Initial Redis connection could not be established. Caching will degrade to the database.");
            }

            return new RedisCacheService(logger, metrics, multiplexer);
        });

        // 4. Persistence services (all scoped: they share the request's DbContext and therefore its transaction)
        services.AddScoped<IUnitOfWork, EfUnitOfWork>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IUserRepository, UserRepository>();
        services.AddScoped<IOrderDashboardRepository, OrderDashboardRepository>();
        services.AddScoped<IOutbox, EfOutbox>();
        services.AddScoped<IIdempotencyStore, IdempotencyStore>();
        services.AddScoped<IOutboxAdminService, OutboxAdminService>();

        // 5. Processing / maintenance services
        services.AddScoped<IOrderDashboardRefreshService, OrderDashboardRefreshService>();
        services.AddScoped<OutboxProcessor>();
        services.AddScoped<RetentionService>();

        // 6. Security
        services.AddSingleton<IPasswordHasherService, IdentityPasswordHasherService>();

        // 7. Hosted background services
        services.AddHostedService<OutboxProcessorBackgroundService>();
        services.AddHostedService<MaintenanceBackgroundService>();

        return services;
    }
}
