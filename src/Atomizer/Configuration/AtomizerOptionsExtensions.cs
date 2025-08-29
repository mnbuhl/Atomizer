using Atomizer.Core;
using Atomizer.Locking;
using Atomizer.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Atomizer;

public static class AtomizerOptionsExtensions
{
    public static AtomizerOptions UseInMemoryStorage(
        this AtomizerOptions options,
        Action<InMemoryJobStorageOptions>? configure = null
    )
    {
        var inMemoryOptions = new InMemoryJobStorageOptions();
        configure?.Invoke(inMemoryOptions);
        options.JobStorageOptions = new JobStorageOptions(sp => new InMemoryStorage(
            inMemoryOptions,
            sp.GetRequiredService<IAtomizerClock>(),
            sp.GetRequiredService<ILogger<InMemoryStorage>>()
        ));
        options.LeasingScopeOptions = new LeasingScopeOptions(sp => new InMemoryLeasingScopeFactory(
            sp.GetRequiredService<IAtomizerClock>()
        ));
        return options;
    }
}
