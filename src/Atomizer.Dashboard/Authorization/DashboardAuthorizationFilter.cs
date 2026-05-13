using Atomizer.Dashboard.Configuration;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;

namespace Atomizer.Dashboard.Authorization;

internal static class DashboardAuthorizationFilter
{
    private static readonly LocalRequestsOnlyAuthorizationFilter DefaultFilter = new();

    public static Delegate Wrap<THandler>(Func<THandler, HttpContext, Task> handler)
        where THandler : notnull
    {
        Func<HttpContext, THandler, IOptions<DashboardOptions>, Task> routeHandler = async (
            context,
            endpointHandler,
            options
        ) =>
        {
            var filters =
                options.Value.Authorization.Count > 0
                    ? options.Value.Authorization
                    : (IEnumerable<IAtomizerDashboardAuthorizationFilter>)[DefaultFilter];

            var denial = DashboardAuthorizationResult.Forbidden;
            foreach (var filter in filters)
            {
                var result = filter.Authorize(context);
                if (result == DashboardAuthorizationResult.Authorized)
                {
                    await handler(endpointHandler, context);
                    return;
                }

                if (result == DashboardAuthorizationResult.Unauthorized)
                    denial = DashboardAuthorizationResult.Unauthorized;
            }

            context.Response.StatusCode = denial == DashboardAuthorizationResult.Unauthorized ? 401 : 403;
        };

        return routeHandler;
    }
}
