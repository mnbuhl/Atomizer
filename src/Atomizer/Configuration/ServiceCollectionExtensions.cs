using System;
using System.Linq;
using Atomizer.Abstractions;
using Atomizer.Hosting;
using Microsoft.Extensions.DependencyInjection;

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

            services.AddSingleton(options);
            services.AddSingleton<IAtomizerClock, AtomizerClock>();
            services.AddSingleton<IRetryPolicy, DefaultRetryPolicy>();
            services.AddSingleton<IJobTypeResolver, DefaultJobTypeResolver>();

            return services;
        }
    }
}
