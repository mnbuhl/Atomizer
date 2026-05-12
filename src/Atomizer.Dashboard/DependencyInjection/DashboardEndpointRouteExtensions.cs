using Atomizer.Dashboard.Authorization;
using Atomizer.Dashboard.Configuration;
using Atomizer.Dashboard.Endpoints;
using Atomizer.Dashboard.StaticFiles;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Atomizer;

/// <summary>
/// Extension methods for mapping Atomizer Dashboard routes.
/// </summary>
public static class DashboardEndpointRouteExtensions
{
    /// <summary>
    /// Maps the Atomizer Dashboard SPA and REST API endpoints.
    /// </summary>
    /// <param name="endpoints">The endpoint route builder.</param>
    /// <param name="routePrefix">The route prefix where the dashboard is mounted. Defaults to <c>/atomizer</c>.</param>
    public static IEndpointConventionBuilder MapAtomizerDashboard(
        this IEndpointRouteBuilder endpoints,
        string routePrefix = "/atomizer"
    )
    {
        var options = endpoints.ServiceProvider.GetRequiredService<IOptions<DashboardOptions>>().Value;

        var prefix = routePrefix.TrimEnd('/');

        endpoints.MapGet(prefix + "/api/jobs", DashboardAuthorizationFilter.Wrap(options, JobsEndpoints.ListAsync));
        endpoints.MapGet(
            prefix + "/api/jobs/{id:guid}",
            DashboardAuthorizationFilter.Wrap(options, JobsEndpoints.GetByIdAsync)
        );
        endpoints.MapGet(
            prefix + "/api/schedules",
            DashboardAuthorizationFilter.Wrap(options, SchedulesEndpoints.ListAsync)
        );
        endpoints.MapGet(
            prefix + "/api/queues/stats",
            DashboardAuthorizationFilter.Wrap(options, QueueStatsEndpoints.GetStatsAsync)
        );
        endpoints.MapGet(
            prefix + "/api/servers",
            DashboardAuthorizationFilter.Wrap(options, ServersEndpoints.ListAsync)
        );

        endpoints.MapGet(
            prefix,
            DashboardAuthorizationFilter.Wrap(options, ctx => EmbeddedSpaFileProvider.ServeIndexAsync(ctx, prefix))
        );

        return endpoints.MapGet(
            prefix + "/{**path}",
            DashboardAuthorizationFilter.Wrap(options, ctx => EmbeddedSpaFileProvider.ServeAsync(ctx, prefix))
        );
    }
}
