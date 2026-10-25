using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using OrderFlow.Application.Common.Behaviors;
using OrderFlow.Application.Interfaces;
using OrderFlow.Application.Services;

namespace OrderFlow.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        // Register MediatR
        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(assembly);
            cfg.AddBehavior(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        });

        // Register FluentValidation
        services.AddValidatorsFromAssembly(assembly);

        // Application services shared by handlers and background jobs
        services.AddScoped<OrderCancellationService>();
        services.AddScoped<IOrderExpirationService, OrderExpirationService>();

        return services;
    }
}
