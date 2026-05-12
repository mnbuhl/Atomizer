using Atomizer.Dashboard.Authorization;

namespace Atomizer.Dashboard.Configuration;

/// <summary>
/// Configuration options for the Atomizer Dashboard.
/// </summary>
public sealed class DashboardOptions
{
    /// <summary>
    /// The route prefix where the dashboard is mounted. Defaults to <c>/atomizer</c>.
    /// </summary>
    public string RoutePrefix { get; set; } = "/atomizer";

    /// <summary>
    /// The title displayed in the dashboard browser tab and heading. Defaults to <c>Atomizer Dashboard</c>.
    /// </summary>
    public string Title { get; set; } = "Atomizer Dashboard";

    /// <summary>
    /// The default page size for the jobs list. Defaults to 50. Hard ceiling of 500.
    /// </summary>
    public int PageSize { get; set; } = 50;

    /// <summary>
    /// How often the queue stats overview automatically refreshes. Defaults to 5 seconds.
    /// </summary>
    public TimeSpan StatsRefreshInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// How often the jobs list automatically refreshes. Defaults to 30 seconds.
    /// </summary>
    public TimeSpan JobsRefreshInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Authorization filters applied to all dashboard requests.
    /// If empty, only localhost requests are allowed by default.
    /// </summary>
    public IList<IAtomizerDashboardAuthorizationFilter> Authorization { get; } =
        new List<IAtomizerDashboardAuthorizationFilter>();
}
