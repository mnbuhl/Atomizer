using Atomizer.Abstractions;
using Microsoft.Extensions.DependencyInjection;

namespace Atomizer;

/// <summary>
/// Holds the factory and lifetime settings for the job storage implementation.
/// </summary>
public class JobStorageOptions
{
    /// <summary>
    /// Initializes a new <see cref="JobStorageOptions"/> with the specified factory and lifetime.
    /// </summary>
    /// <param name="jobStorageFactory">Factory delegate used to create the <see cref="IAtomizerStorage"/> instance.</param>
    /// <param name="jobStorageLifetime">The DI lifetime for the storage instance.
    /// <remarks>Defaults to <see cref="ServiceLifetime.Singleton"/>.</remarks>
    /// </param>
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
