using Atomizer.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Atomizer;

/// <summary>
/// Holds the factory and lifetime for the <see cref="IAtomizerStorage"/> implementation.
/// </summary>
public class JobStorageOptions
{
    /// <summary>
    /// Initializes a new instance of <see cref="JobStorageOptions"/>.
    /// </summary>
    /// <param name="jobStorageFactory">Factory delegate that creates the storage instance from the service provider.</param>
    /// <param name="jobStorageLifetime">The DI lifetime for the storage service. Defaults to <see cref="ServiceLifetime.Singleton"/>.</param>
    public JobStorageOptions(
        Func<IServiceProvider, IAtomizerStorage> jobStorageFactory,
        ServiceLifetime jobStorageLifetime = ServiceLifetime.Singleton
    )
    {
        JobStorageFactory = jobStorageFactory;
        JobStorageLifetime = jobStorageLifetime;
    }

    /// <summary>
    /// Function to create the job storage.
    /// </summary>
    public Func<IServiceProvider, IAtomizerStorage> JobStorageFactory { get; set; }

    /// <summary>
    /// Gets or sets the lifetime of the job storage service.
    /// </summary>
    public ServiceLifetime JobStorageLifetime { get; set; }
}
