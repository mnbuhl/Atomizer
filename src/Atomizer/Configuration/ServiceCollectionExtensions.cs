using System;
using System.Linq;
using Atomizer.Abstractions;
using Atomizer.Core;
using Atomizer.Exceptions;
using Atomizer.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Atomizer
{
    public static class ServiceCollectionExtensions
    {
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
            services.AddSingleton<IAtomizerStorageScopeFactory, ServiceProviderStorageScopeFactory>();

            switch (options.JobStorageOptions.JobStorageLifetime)
            {
                case ServiceLifetime.Singleton:
                    services.AddSingleton(options.JobStorageOptions.JobStorageFactory);
                    break;
                case ServiceLifetime.Scoped:
                    services.AddScoped(options.JobStorageOptions.JobStorageFactory);
                    break;
                case ServiceLifetime.Transient:
                    services.AddTransient(options.JobStorageOptions.JobStorageFactory);
                    break;
            }

            return services;
        }

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
            services.AddSingleton<IQueueCoordinator, QueueCoordinator>();
            services.AddHostedService<AtomizerQueueService>();
            services.AddSingleton<IScheduler, Scheduler>();
            services.AddHostedService<AtomizerSchedulerService>();
            services.AddSingleton<AtomizerRuntimeIdentity>();

            services.AddSingleton<IQueuePumpFactory, QueuePumpFactory>();
            services.AddSingleton<IQueuePoller, QueuePoller>();
            services.AddSingleton<IJobWorkerFactory, JobWorkerFactory>();
            services.AddSingleton<IJobProcessorFactory, JobProcessorFactory>();

            return services;
        }
    }
}
