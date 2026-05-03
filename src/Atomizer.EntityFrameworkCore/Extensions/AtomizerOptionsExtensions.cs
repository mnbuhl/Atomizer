using Atomizer.Core;
using Atomizer.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Atomizer.EntityFrameworkCore;

/// <summary>
/// Extension methods for configuring the Entity Framework Core storage backend on <see cref="AtomizerOptions"/>.
/// </summary>
public static class AtomizerOptionsExtensions
{
    /// <summary>
    /// Configures Atomizer to use the Entity Framework Core storage backend backed by <typeparamref name="TDbContext"/>.
    /// </summary>
    /// <typeparam name="TDbContext">The <see cref="DbContext"/> type to use as the backing store.</typeparam>
    /// <param name="options">The <see cref="AtomizerOptions"/> to configure.</param>
    /// <param name="configure">Optional delegate to configure EF Core storage options.</param>
    /// <returns>The configured <see cref="AtomizerOptions"/> instance for chaining.</returns>
    public static AtomizerOptions UseEntityFrameworkCoreStorage<TDbContext>(
        this AtomizerOptions options,
        Action<EntityFrameworkCoreJobStorageOptions>? configure = null
    )
        where TDbContext : DbContext
    {
        var efOptions = new EntityFrameworkCoreJobStorageOptions();
        configure?.Invoke(efOptions);

        options.JobStorageOptions = new JobStorageOptions(
            sp => new EntityFrameworkCoreStorage<TDbContext>(
                sp.GetRequiredService<TDbContext>(),
                efOptions,
                sp.GetRequiredService<ILogger<EntityFrameworkCoreStorage<TDbContext>>>(),
                sp.GetRequiredService<IAtomizerClock>()
            ),
            ServiceLifetime.Scoped
        );

        return options;
    }
}
