using Microsoft.AspNetCore.Http;

namespace Atomizer.Dashboard.Authorization;

/// <summary>
/// Authorizes incoming HTTP requests to the Atomizer Dashboard.
/// The first filter that returns <see cref="DashboardAuthorizationResult.Authorized"/> allows the request.
/// If all filters return a non-authorized result the most specific denial wins:
/// <see cref="DashboardAuthorizationResult.Unauthorized"/> (401) takes precedence over
/// <see cref="DashboardAuthorizationResult.Forbidden"/> (403).
/// If no filters are configured, only localhost requests are permitted.
/// </summary>
public interface IAtomizerDashboardAuthorizationFilter
{
    /// <summary>Evaluates whether the request should be allowed.</summary>
    DashboardAuthorizationResult Authorize(HttpContext context);
}
