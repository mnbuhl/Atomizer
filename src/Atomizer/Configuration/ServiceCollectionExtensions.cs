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
    /// Registers core Atomizer services including storage, client, serializer, and job handlers.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="configure">Optional delegate to configure <see cref="AtomizerOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
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
    /// Registers the Atomizer background processing pipeline including queue and scheduler hosted services.
    /// </summary>
    /// <param name="services">The service collection to add services to.</param>
    /// <param name="configure">Optional delegate to configure <see cref="AtomizerProcessingOptions"/>.</param>
    /// <returns>The service collection for chaining.</returns>
    public static IServiceCollection AddAtomizerProcessing(
        this IServiceCollection services,
        Action<AtomizerProcessingOptions>? configure = null
    )
    {
        var options = new AtomizerProcessingOptions();
        configure?.Invoke(options);

        options.Validate();

        services.AddSingleton(options);
        services.AddSingleton<AtomizerRuntimeIdentity>();
        services.AddHostedService<AtomizerHeartbeatRecoveryService>();
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
