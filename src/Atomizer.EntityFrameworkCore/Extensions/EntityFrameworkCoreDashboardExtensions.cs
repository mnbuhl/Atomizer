using Atomizer.Core;
using Atomizer.Dashboard;
using Atomizer.EntityFrameworkCore.Dashboard;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Atomizer.EntityFrameworkCore;

/// <summary>
/// Extension methods for wiring the Atomizer Dashboard with the Entity Framework Core storage backend.
/// </summary>
public static class EntityFrameworkCoreDashboardExtensions
{
    /// <summary>
    /// Replaces the default in-memory dashboard storage adapter with an Entity Framework Core adapter.
    /// Call this after <c>services.AddAtomizerDashboard()</c> when using EF Core storage.
    /// </summary>
    /// <typeparam name="TContext">The application's <see cref="DbContext"/> that has Atomizer entities registered.</typeparam>
    public static IServiceCollection UseEntityFrameworkCoreDashboardStorage<TContext>(this IServiceCollection services)
        where TContext : DbContext
    {
        services.Replace(
            ServiceDescriptor.Singleton<IAtomizerDashboardStorage>(
                sp => new EntityFrameworkCoreDashboardStorage<TContext>(
                    sp.GetRequiredService<IDbContextFactory<TContext>>(),
                    sp.GetRequiredService<IAtomizerClock>()
                )
            )
        );

        return services;
    }
}
