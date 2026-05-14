namespace Atomizer.Dashboard.Configuration;

/// <summary>
/// Configures client-side behavior for the embedded Atomizer Dashboard SPA.
/// </summary>
public sealed class DashboardClientOptions
{
    /// <summary>
    /// Headers that the embedded dashboard frontend attaches to dashboard API requests.
    /// </summary>
    /// <remarks>
    /// Use these headers for same-origin request metadata such as CSRF tokens or short-lived per-user tokens.
    /// Do not put long-lived shared secrets in the dashboard HTML.
    /// </remarks>
    public DashboardClientRequestHeaderCollection RequestHeaders { get; } = new();
}
