using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using OrderFlow.Application.Common.Options;
using OrderFlow.Application.Interfaces;
using OrderFlow.Infrastructure.BackgroundServices;
using OrderFlow.Infrastructure.Caching;
using OrderFlow.Infrastructure.Persistence;
using OrderFlow.Infrastructure.Repositories;
using OrderFlow.Infrastructure.Services;
using StackExchange.Redis;

namespace OrderFlow.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        // 1. Configure Options
        services.Configure<CachingOptions>(configuration.GetSection(CachingOptions.SectionName));
        services.Configure<RedisOptions>(configuration.GetSection(RedisOptions.SectionName));
        services.Configure<BackgroundJobOptions>(configuration.GetSection(BackgroundJobOptions.SectionName));

        // 2. Configure SQL Server DbContext
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

        // 3. Configure Redis Connection Multiplexer and Caching Service with resilient fallback
        services.AddSingleton<ICacheService>(sp =>
        {
            var logger = sp.GetRequiredService<ILogger<RedisCacheService>>();
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
                config.SyncTimeout = 3000;

                multiplexer = ConnectionMultiplexer.Connect(config);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Initial Redis connection could not be established. Redis caching will fall back gracefully.");
            }

            return new RedisCacheService(logger, multiplexer);
        });

        // 5. Register Repositories
        services.AddScoped<IOrderRepository, OrderRepository>();
        services.AddScoped<IProductRepository, ProductRepository>();
        services.AddScoped<ICustomerRepository, CustomerRepository>();
        services.AddScoped<IOrderDashboardRepository, OrderDashboardRepository>();

        // 6. Register Application / Infrastructure Processing Services
        services.AddScoped<IOrderProcessingService, OrderProcessingService>();
        services.AddScoped<IOrderDashboardRefreshService, OrderDashboardRefreshService>();

        // 7. Register Hosted Background Service
        services.AddHostedService<OrderProcessingBackgroundService>();

        return services;
    }
}
