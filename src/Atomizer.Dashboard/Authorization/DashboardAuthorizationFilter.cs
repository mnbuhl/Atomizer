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
            var result =
                options.Value.Authorization.Count > 0
                    ? await options.Value.Authorization.AuthorizeAsync(context)
                    : await ((IAtomizerDashboardAuthorizationFilter)DefaultFilter).AuthorizeAsync(context);

            if (result == DashboardAuthorizationResult.Authorized)
            {
                await handler(endpointHandler, context);
                return;
            }

            context.Response.StatusCode = result == DashboardAuthorizationResult.Unauthorized ? 401 : 403;
        };

        return routeHandler;
    }
}
