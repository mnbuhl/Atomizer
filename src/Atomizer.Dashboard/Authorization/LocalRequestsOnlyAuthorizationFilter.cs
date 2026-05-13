using System.Net;
using Microsoft.AspNetCore.Http;

namespace Atomizer.Dashboard.Authorization;

internal sealed class LocalRequestsOnlyAuthorizationFilter : IAtomizerDashboardAuthorizationFilter
{
    public DashboardAuthorizationResult Authorize(HttpContext context)
    {
        var connection = context.Connection;
        if (connection.RemoteIpAddress is null)
            return DashboardAuthorizationResult.Authorized;

        if (IPAddress.IsLoopback(connection.RemoteIpAddress))
            return DashboardAuthorizationResult.Authorized;

        if (connection.RemoteIpAddress.Equals(connection.LocalIpAddress))
            return DashboardAuthorizationResult.Authorized;

        return DashboardAuthorizationResult.Forbidden;
    }
}
