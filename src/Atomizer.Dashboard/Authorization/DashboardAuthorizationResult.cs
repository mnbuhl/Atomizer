namespace Atomizer.Dashboard.Authorization;

/// <summary>
/// The result of an <see cref="IAtomizerDashboardAuthorizationFilter"/> authorization check.
/// </summary>
public enum DashboardAuthorizationResult
{
    /// <summary>The request is authorized and may proceed.</summary>
    Authorized,

    /// <summary>
    /// The request lacks valid credentials. Responds with HTTP 401.
    /// Use when the caller could supply credentials to gain access.
    /// </summary>
    Unauthorized,

    /// <summary>
    /// The request is authenticated but not permitted. Responds with HTTP 403.
    /// Use when the caller's identity is known but access is denied.
    /// </summary>
    Forbidden,
}
