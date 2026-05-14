using Atomizer.Dashboard.Configuration;
using Atomizer.Dashboard.Endpoints;
using Atomizer.Dashboard.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Atomizer;

/// <summary>
/// Extension methods for registering Atomizer Dashboard services.
/// </summary>
public static class DashboardServiceCollectionExtensions
{
    /// <summary>
    /// Adds the Atomizer Dashboard services to the dependency injection container.
    /// Call <see cref="DashboardEndpointRouteExtensions.MapAtomizerDashboard"/> to map routes.
    /// </summary>
    /// <remarks>
    /// The dashboard exposes job payloads. If no authorization is configured, requests are limited to localhost.
    /// Configure <see cref="DashboardOptions.Authorization"/> before exposing the dashboard outside local development.
    /// </remarks>
    public static IServiceCollection AddAtomizerDashboard(
        this IServiceCollection services,
        Action<DashboardOptions>? configure = null
    )
    {
        services.AddOptions<DashboardOptions>().Configure(configure ?? (_ => { }));
        services.TryAddScoped<DashboardCommandService>();
        services.TryAddScoped<JobsEndpointHandler>();
        services.TryAddScoped<SchedulesEndpointHandler>();
        services.TryAddScoped<QueueStatsEndpointHandler>();
        services.TryAddScoped<ServersEndpointHandler>();
        services.TryAddScoped<StaticFilesEndpointHandler>();

        return services;
    }
}
