using System;
using Atomizer.Abstractions;
using Atomizer.Configuration;
using Atomizer.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Atomizer.EntityFrameworkCore.Extensions
{
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
                sp => new EntityFrameworkCoreJobStorage<TDbContext>(
                    sp.GetRequiredService<TDbContext>(),
                    efOptions,
                    sp.GetRequiredService<IAtomizerLogger<EntityFrameworkCoreJobStorage<TDbContext>>>()
                ),
                ServiceLifetime.Scoped
            );

            return options;
        }
    }
}
