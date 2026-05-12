using Atomizer.Dashboard.Configuration;
using Microsoft.AspNetCore.Http;

namespace Atomizer.Dashboard.Authorization;

internal static class DashboardAuthorizationFilter
{
    private static readonly LocalRequestsOnlyAuthorizationFilter DefaultFilter = new();

    public static RequestDelegate Wrap(DashboardOptions options, RequestDelegate handler) =>
        async context =>
        {
            var filters =
                options.Authorization.Count > 0
                    ? options.Authorization
                    : (IEnumerable<IAtomizerDashboardAuthorizationFilter>)[DefaultFilter];

            var denial = DashboardAuthorizationResult.Forbidden;
            foreach (var filter in filters)
            {
                var result = filter.Authorize(context);
                if (result == DashboardAuthorizationResult.Authorized)
                {
                    await handler(context);
                    return;
                }

                if (result == DashboardAuthorizationResult.Unauthorized)
                    denial = DashboardAuthorizationResult.Unauthorized;
            }

            context.Response.StatusCode = denial == DashboardAuthorizationResult.Unauthorized ? 401 : 403;
        };
}
