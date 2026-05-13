using Atomizer.Dashboard.Authorization;

internal sealed class AlwaysAllowAuthFilter : IAtomizerDashboardAuthorizationFilter
{
    public DashboardAuthorizationResult Authorize(HttpContext context) => DashboardAuthorizationResult.Authorized;
}
