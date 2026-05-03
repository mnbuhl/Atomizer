using Atomizer.Core;
using Atomizer.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Atomizer;

/// <summary>
/// Extension methods for configuring storage backends on <see cref="AtomizerOptions"/>.
/// </summary>
public static class AtomizerOptionsExtensions
{
    /// <summary>
    /// Configures Atomizer to use the in-memory storage backend.
    /// </summary>
    /// <param name="options">The <see cref="AtomizerOptions"/> to configure.</param>
    /// <param name="configure">Optional delegate to configure in-memory storage options.</param>
    /// <returns>The configured <see cref="AtomizerOptions"/> instance for chaining.</returns>
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
        return options;
    }
}
