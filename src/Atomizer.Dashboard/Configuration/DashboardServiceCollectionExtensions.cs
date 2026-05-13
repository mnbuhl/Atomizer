using Atomizer.Dashboard;
using Atomizer.Dashboard.Configuration;
using Atomizer.Dashboard.Storage;
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
    /// WARNING: The dashboard exposes job payloads and is not authenticated in v1.
    /// Do not use in production environments without configuring authorization filters.
    /// </remarks>
    public static IServiceCollection AddAtomizerDashboard(
        this IServiceCollection services,
        Action<DashboardOptions>? configure = null
    )
    {
        services.AddOptions<DashboardOptions>().Configure(configure ?? (_ => { }));
        services.TryAddSingleton<IAtomizerDashboardStorage, InMemoryDashboardStorage>();

        return services;
    }
}
