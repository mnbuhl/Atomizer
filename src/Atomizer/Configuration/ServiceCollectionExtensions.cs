using System;
using System.Linq;
using Atomizer.Abstractions;
using Atomizer.Hosting;
using Atomizer.Processing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Atomizer.Configuration
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

            if (options.Queues.All(q => !Equals(q.QueueKey, QueueKey.Default)))
            {
                options.AddQueue(QueueKey.Default);
            }

            if (options.JobStorageFactory is null)
            {
                throw new InvalidOperationException(
                    "JobStorageFactory must be set. Use UseInMemoryStorage or another storage provider."
                );
            }

            services.AddSingleton(options);
            services.Add(options.Handlers);
            services.AddSingleton<IAtomizerClock, AtomizerClock>();
            services.AddSingleton<IRetryPolicy, DefaultRetryPolicy>();
            services.AddSingleton<IJobTypeResolver, DefaultJobTypeResolver>();
            services.AddSingleton<IJobDispatcher, DefaultJobDispatcher>();
            services.AddSingleton<IJobSerializer, DefaultJobSerializer>();
            services.AddSingleton(typeof(IAtomizerLogger<>), typeof(DefaultAtomizerLogger<>));
            services.AddSingleton<IAtomizerServiceResolver, DefaultAtomizerServiceResolver>();

            switch (options.JobStorageLifetime)
            {
                case ServiceLifetime.Singleton:
                    services.AddSingleton(options.JobStorageFactory);
                    break;
                case ServiceLifetime.Scoped:
                    services.AddScoped(options.JobStorageFactory);
                    break;
                case ServiceLifetime.Transient:
                    services.AddTransient(options.JobStorageFactory);
                    break;
            }

            if (options.EnableProcessing)
            {
                services.AddSingleton<IQueueCoordinator, QueueCoordinator>();
                services.AddHostedService<AtomizerHostedService>();
            }

            return services;
        }
    }
}
