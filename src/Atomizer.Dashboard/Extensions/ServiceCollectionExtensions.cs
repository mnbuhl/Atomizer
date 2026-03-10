using Atomizer.Abstractions;
using Atomizer.Dashboard.Abstractions;
using Atomizer.Dashboard.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Atomizer.Dashboard;

/// <summary>
/// Extension methods for registering Atomizer Dashboard services with the
/// ASP.NET Core dependency-injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Adds all services required to host the Atomizer Dashboard, including
    /// Blazor Server infrastructure and dashboard-specific services.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Call this method <em>after</em> <c>AddAtomizer()</c> so that a concrete
    /// <see cref="IAtomizerStorage"/> — which must also implement
    /// <see cref="IAtomizerDashboardStorage"/> — is already present in the container.
    /// Both built-in backends (InMemory and EF Core) satisfy this requirement.
    /// </para>
    /// <para>
    /// <see cref="IDashboardStorage"/> is registered as a scoped service backed by a
    /// <see cref="DashboardStorageAdapter"/> that wraps the host application's
    /// <see cref="IAtomizerDashboardStorage"/> implementation and adds filtered-query
    /// support for the job-browser view. To use a custom high-performance implementation,
    /// register your own <see cref="IDashboardStorage"/> <em>after</em> calling this method.
    /// </para>
    /// </remarks>
    /// <param name="services">The <see cref="IServiceCollection"/> to configure.</param>
    /// <param name="configure">
    /// An optional delegate used to configure <see cref="DashboardOptions"/>.
    /// </param>
    /// <returns>The same <see cref="IServiceCollection"/> for method chaining.</returns>
    public static IServiceCollection AddAtomizerDashboard(
        this IServiceCollection services,
        Action<DashboardOptions>? configure = null
    )
    {
        var options = new DashboardOptions();
        configure?.Invoke(options);

        services.AddSingleton(options);

        services.AddRazorComponents().AddInteractiveServerComponents();

        // Register the storage adapter that bridges IAtomizerDashboardStorage (core) →
        // IDashboardStorage (dashboard) and adds in-memory filtered job queries used by
        // the job-browser view.
        services.AddScoped<IDashboardStorage>(sp =>
        {
            var storage = sp.GetRequiredService<IAtomizerStorage>();

            var dashboardStorage =
                storage as IAtomizerDashboardStorage
                ?? throw new InvalidOperationException(
                    $"The registered {nameof(IAtomizerStorage)} implementation "
                        + $"({storage.GetType().Name}) does not implement "
                        + $"{nameof(IAtomizerDashboardStorage)}. "
                        + "Use a dashboard-compatible backend: UseInMemoryStorage() or "
                        + "UseEntityFrameworkCoreStorage()."
                );

            return new DashboardStorageAdapter(dashboardStorage);
        });

        services.AddScoped<QueueStatsService>();
        services.AddScoped<ScheduleSummaryService>();
        services.AddScoped<ScheduleTriggerService>();
        services.AddScoped<CancelJobService>();

        return services;
    }
}
