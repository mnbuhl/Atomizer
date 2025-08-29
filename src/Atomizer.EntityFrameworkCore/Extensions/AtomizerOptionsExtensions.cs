using Atomizer.Core;
using Atomizer.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Atomizer.EntityFrameworkCore;

public static class AtomizerOptionsExtensions
{
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
                sp.GetRequiredService<ILogger<EntityFrameworkCoreStorage<TDbContext>>>()
            ),
            ServiceLifetime.Scoped
        );

        options.LeasingScopeOptions = new LeasingScopeOptions(
            sp => new DatabaseTransactionLeasingScopeFactory<TDbContext>(
                sp.GetRequiredService<TDbContext>(),
                sp.GetRequiredService<ILogger<DatabaseTransactionLeasingScopeFactory<TDbContext>>>()
            ),
            ServiceLifetime.Scoped
        );

        return options;
    }
}
