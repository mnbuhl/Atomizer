using System;
using Atomizer.Abstractions;
using Atomizer.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Atomizer.Storage
{
    public static class AtomizerOptionsExtensions
    {
        public static AtomizerOptions UseInMemoryStorage(
            this AtomizerOptions options,
            Action<InMemoryJobStorageOptions>? configure = null
        )
        {
            var inMemoryOptions = new InMemoryJobStorageOptions();
            configure?.Invoke(inMemoryOptions);
            options.JobStorageOptions = new JobStorageOptions(sp => new InMemoryJobStorage(
                inMemoryOptions,
                sp.GetRequiredService<IAtomizerLogger<InMemoryJobStorage>>()
            ));
            return options;
        }
    }
}
