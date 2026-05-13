using Atomizer.Dashboard.Authorization;
using Atomizer.Dashboard.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;

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
        var prefix = routePrefix.TrimEnd('/');

        endpoints.MapGet(
            prefix + "/api/jobs",
            DashboardAuthorizationFilter.Wrap<JobsEndpointHandler>((handler, ctx) => handler.ListAsync(ctx))
        );
        endpoints.MapGet(
            prefix + "/api/jobs/{id:guid}",
            DashboardAuthorizationFilter.Wrap<JobsEndpointHandler>((handler, ctx) => handler.GetByIdAsync(ctx))
        );
        endpoints.MapGet(
            prefix + "/api/schedules",
            DashboardAuthorizationFilter.Wrap<SchedulesEndpointHandler>((handler, ctx) => handler.ListAsync(ctx))
        );
        endpoints.MapGet(
            prefix + "/api/queues/stats",
            DashboardAuthorizationFilter.Wrap<QueueStatsEndpointHandler>((handler, ctx) => handler.GetStatsAsync(ctx))
        );
        endpoints.MapGet(
            prefix + "/api/servers",
            DashboardAuthorizationFilter.Wrap<ServersEndpointHandler>((handler, ctx) => handler.ListAsync(ctx))
        );

        endpoints.MapGet(
            prefix,
            DashboardAuthorizationFilter.Wrap<StaticFilesEndpointHandler>(
                (handler, ctx) => handler.ServeIndexAsync(ctx, prefix)
            )
        );

        return endpoints.MapGet(
            prefix + "/{**path}",
            DashboardAuthorizationFilter.Wrap<StaticFilesEndpointHandler>(
                (handler, ctx) => handler.ServeAsync(ctx, prefix)
            )
        );
    }
}
