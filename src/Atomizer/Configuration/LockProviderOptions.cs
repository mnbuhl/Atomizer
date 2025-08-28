using Atomizer.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Atomizer;

public class LockProviderOptions
{
    public LockProviderOptions(
        Func<IServiceProvider, IAtomizerLockProvider> lockProviderFactory,
        ServiceLifetime lockProviderLifetime = ServiceLifetime.Singleton
    )
    {
        LockProviderFactory = lockProviderFactory;
        LockProviderLifetime = lockProviderLifetime;
    }

    /// <summary>
    /// Function to create the lock provider.
    /// </summary>
    public Func<IServiceProvider, IAtomizerLockProvider> LockProviderFactory { get; set; }

    /// <summary>
    /// Gets or sets the lifetime of the lock provider service.
    /// </summary>
    public ServiceLifetime LockProviderLifetime { get; set; }
}
