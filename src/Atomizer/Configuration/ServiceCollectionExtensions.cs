using Atomizer.Abstractions;
using Atomizer.Core;
using Atomizer.Exceptions;
using Atomizer.Processing;
using Atomizer.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Atomizer;

/// <summary>
/// Extension methods for registering Atomizer services with the dependency injection container.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers Atomizer core services including storage, client, serialization, and handler resolution.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="configure">Optional delegate to configure <see cref="AtomizerOptions"/>.</param>
    /// <returns>The same <see cref="IServiceCollection"/> instance for chaining.</returns>
    public static IServiceCollection AddAtomizer(
        this IServiceCollection services,
        Action<AtomizerOptions>? configure = null
    )
    {
        var options = new AtomizerOptions();
        configure?.Invoke(options);

        if (options.JobStorageOptions is null)
        {
            throw new InvalidAtomizerConfigurationException(
                "JobStorageFactory must be set. Use UseInMemoryStorage or another storage provider."
            );
        }

        if (options.Queues.Count == 0 || options.Queues.All(q => q.QueueKey != QueueKey.Default))
        {
            options.AddQueue(QueueKey.Default);
        }

        services.AddSingleton(options);
        services.Add(options.Handlers);
        services.AddSingleton<IAtomizerClient, AtomizerClient>();
        services.AddSingleton<IAtomizerClock, AtomizerClock>();
        services.AddSingleton<IAtomizerJobTypeResolver, DefaultJobTypeResolver>();
        services.AddSingleton<IAtomizerJobDispatcher, DefaultJobDispatcher>();
        services.AddSingleton<IAtomizerJobSerializer, DefaultJobSerializer>();
        services.AddSingleton<IAtomizerServiceScopeFactory, ServiceProviderServiceScopeFactory>();

        services.Add(
            ServiceDescriptor.Describe(
                typeof(IAtomizerStorage),
                options.JobStorageOptions.JobStorageFactory,
                options.JobStorageOptions.JobStorageLifetime
            )
        );

        return services;
    }

    /// <summary>
    /// Registers Atomizer processing services including queue workers, coordinator, and scheduler.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/> to add services to.</param>
    /// <param name="configure">Optional delegate to configure <see cref="AtomizerProcessingOptions"/>.</param>
    /// <returns>The same <see cref="IServiceCollection"/> instance for chaining.</returns>
    public static IServiceCollection AddAtomizerProcessing(
        this IServiceCollection services,
        Action<AtomizerProcessingOptions>? configure = null
    )
    {
        var options = new AtomizerProcessingOptions();
        configure?.Invoke(options);

        if (options.StartupDelay != null && options.StartupDelay < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.StartupDelay),
                "Startup delay must be a non-negative TimeSpan."
            );
        }

        services.AddSingleton(options);
        services.AddSingleton<AtomizerRuntimeIdentity>();
        services.AddHostedService<AtomizerQueueService>();
        services.AddSingleton<IQueueCoordinator, QueueCoordinator>();
        services.AddSingleton<IQueuePumpFactory, QueuePumpFactory>();
        services.AddSingleton<IQueuePoller, QueuePoller>();
        services.AddSingleton<IJobWorkerFactory, JobWorkerFactory>();
        services.AddSingleton<IJobProcessorFactory, JobProcessorFactory>();

        services.AddSingleton<IScheduler, Scheduler>();
        services.AddHostedService<AtomizerSchedulerService>();
        services.AddSingleton<ISchedulePoller, SchedulePoller>();
        services.AddSingleton<IScheduleProcessor, ScheduleProcessor>();

        return services;
    }
}
