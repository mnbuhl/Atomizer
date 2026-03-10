namespace Atomizer.Dashboard;

/// <summary>
/// Configuration options for the Atomizer Dashboard.
/// Pass an instance to <see cref="ServiceCollectionExtensions.AddAtomizerDashboard"/>
/// or to <c>MapAtomizerDashboard</c> to customise the dashboard behaviour.
/// </summary>
public sealed class DashboardOptions
{
    /// <summary>
    /// The path prefix under which all dashboard routes are served.
    /// Must start with a forward slash and must not end with one.
    /// Defaults to <c>/atomizer</c>.
    /// </summary>
    public string BasePath { get; set; } = "/atomizer";

    /// <summary>
    /// When set, the dashboard middleware will require that the incoming request
    /// satisfies the named ASP.NET Core authorization policy before rendering any
    /// page. Leave <see langword="null"/> (the default) to allow anonymous access.
    /// </summary>
    /// <remarks>
    /// The named policy must be registered in the host application's authorization
    /// infrastructure via <c>services.AddAuthorization(o =&gt; o.AddPolicy(...))</c>
    /// before <c>MapAtomizerDashboard</c> is called.
    /// </remarks>
    public string? AuthorizationPolicyName { get; set; }

    /// <summary>
    /// How frequently the Queue Statistics page polls storage for updated counts.
    /// The interval drives the <see cref="System.Threading.PeriodicTimer"/> inside
    /// the Queues Blazor component, so changes take effect on the next page load.
    /// Defaults to <c>5 seconds</c>. Must be greater than <see cref="TimeSpan.Zero"/>.
    /// </summary>
    public TimeSpan StatsRefreshInterval { get; set; } = TimeSpan.FromSeconds(5);
}
