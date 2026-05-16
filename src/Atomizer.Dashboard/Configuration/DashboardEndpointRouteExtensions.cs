using Atomizer.Dashboard.Authorization;
using Atomizer.Dashboard.Endpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
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

        MapDashboardGet(
            endpoints,
            prefix + "/api/jobs",
            DashboardAuthorizationFilter.Wrap<JobsEndpointHandler>((handler, ctx) => handler.ListAsync(ctx))
        );
        MapDashboardGet(
            endpoints,
            prefix + "/api/jobs/{id:guid}",
            DashboardAuthorizationFilter.Wrap<JobsEndpointHandler>((handler, ctx) => handler.GetByIdAsync(ctx))
        );
        MapDashboardGet(
            endpoints,
            prefix + "/api/job-types",
            DashboardAuthorizationFilter.Wrap<JobsEndpointHandler>((handler, ctx) => handler.ListJobTypesAsync(ctx))
        );
        MapDashboardPost(
            endpoints,
            prefix + "/api/jobs/trigger",
            DashboardAuthorizationFilter.Wrap<JobsEndpointHandler>((handler, ctx) => handler.TriggerAsync(ctx))
        );
        MapDashboardPost(
            endpoints,
            prefix + "/api/jobs/{id:guid}/retry",
            DashboardAuthorizationFilter.Wrap<JobsEndpointHandler>((handler, ctx) => handler.RetryAsync(ctx))
        );
        MapDashboardPost(
            endpoints,
            prefix + "/api/jobs/{id:guid}/cancel",
            DashboardAuthorizationFilter.Wrap<JobsEndpointHandler>((handler, ctx) => handler.CancelAsync(ctx))
        );
        MapDashboardGet(
            endpoints,
            prefix + "/api/schedules",
            DashboardAuthorizationFilter.Wrap<SchedulesEndpointHandler>((handler, ctx) => handler.ListAsync(ctx))
        );
        MapDashboardPost(
            endpoints,
            prefix + "/api/schedules/{id:guid}/enabled",
            DashboardAuthorizationFilter.Wrap<SchedulesEndpointHandler>((handler, ctx) => handler.SetEnabledAsync(ctx))
        );
        MapDashboardPost(
            endpoints,
            prefix + "/api/schedules/{id:guid}/run-now",
            DashboardAuthorizationFilter.Wrap<SchedulesEndpointHandler>((handler, ctx) => handler.RunNowAsync(ctx))
        );
        MapDashboardGet(
            endpoints,
            prefix + "/api/queues/stats",
            DashboardAuthorizationFilter.Wrap<QueueStatsEndpointHandler>((handler, ctx) => handler.GetStatsAsync(ctx))
        );
        MapDashboardGet(
            endpoints,
            prefix + "/api/servers",
            DashboardAuthorizationFilter.Wrap<ServersEndpointHandler>((handler, ctx) => handler.ListAsync(ctx))
        );

        MapDashboardGet(
            endpoints,
            prefix,
            DashboardAuthorizationFilter.Wrap<StaticFilesEndpointHandler>(
                (handler, ctx) => handler.ServeIndexAsync(ctx, prefix)
            )
        );

        return MapDashboardGet(
            endpoints,
            prefix + "/{**path}",
            DashboardAuthorizationFilter.Wrap<StaticFilesEndpointHandler>(
                (handler, ctx) => handler.ServeAsync(ctx, prefix)
            )
        );
    }

    private static RouteHandlerBuilder MapDashboardGet(
        IEndpointRouteBuilder endpoints,
        string pattern,
        Delegate handler
    )
    {
        return endpoints.MapGet(pattern, handler).ExcludeFromDescription();
    }

    private static RouteHandlerBuilder MapDashboardPost(
        IEndpointRouteBuilder endpoints,
        string pattern,
        Delegate handler
    )
    {
        return endpoints.MapPost(pattern, handler).ExcludeFromDescription();
    }
}
