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
    /// <summary>
    /// Asynchronously evaluates whether the request should be allowed.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    ValueTask<DashboardAuthorizationResult> AuthorizeAsync(HttpContext context) =>
        ValueTask.FromResult(Authorize(context));

    /// <summary>
    /// Evaluates whether the request should be allowed.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <remarks>
    /// Implement this for simple synchronous filters. Implement <see cref="AuthorizeAsync"/> for filters that
    /// need asynchronous work.
    /// </remarks>
    DashboardAuthorizationResult Authorize(HttpContext context) =>
        throw new NotSupportedException(
            $"Implement {nameof(AuthorizeAsync)} for asynchronous dashboard authorization filters."
        );
}
