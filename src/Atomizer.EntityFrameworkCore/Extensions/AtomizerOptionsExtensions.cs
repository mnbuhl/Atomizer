using Atomizer.Core;
using Atomizer.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Atomizer.EntityFrameworkCore;

/// <summary>
/// Extension methods for configuring the Entity Framework Core storage backend with Atomizer.
/// </summary>
public static class AtomizerOptionsExtensions
{
    /// <summary>
    /// Configures Atomizer to use Entity Framework Core as its storage backend.
    /// </summary>
    /// <typeparam name="TDbContext">The <see cref="DbContext"/> type that contains the Atomizer entity sets.</typeparam>
    /// <param name="options">The <see cref="AtomizerOptions"/> to configure.</param>
    /// <param name="configure">Optional delegate to configure <see cref="EntityFrameworkCoreJobStorageOptions"/>.</param>
    /// <returns>The <see cref="AtomizerOptions"/> instance for chaining.</returns>
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
