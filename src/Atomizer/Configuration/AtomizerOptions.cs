using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Atomizer.Configuration
{
    public sealed class AtomizerOptions
    {
        /// <summary>
        /// Gets or sets the interval at which the atomizer ticks.
        /// <remarks>Default is 1 second, meaning that the atomizer will tick every second.</remarks>
        /// </summary>
        public TimeSpan TickInterval { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>
        /// Gets or sets the interval at which the atomizer checks for storage updates.
        /// <remarks>Default is 15 seconds, meaning that the atomizer will check for storage updates every 15 seconds.</remarks>
        /// </summary>
        public TimeSpan StorageCheckInterval { get; set; } = TimeSpan.FromSeconds(15);

        public JobStorageOptions? JobStorageOptions { get; set; }

        internal List<QueueOptions> Queues { get; } = new List<QueueOptions>();
        internal List<ServiceDescriptor> Handlers { get; } = new List<ServiceDescriptor>();

        public AtomizerOptions AddQueue(string name, Action<QueueOptions>? configure = null)
        {
            var options = new QueueOptions(name);
            configure?.Invoke(options);

            if (options.QueueKey == null)
            {
                throw new InvalidOperationException("Queue must be specified.");
            }

            if (options.BatchSize <= 0)
            {
                throw new InvalidOperationException("Batch size must be greater than zero.");
            }

            if (options.DegreeOfParallelism <= 0)
            {
                throw new InvalidOperationException("Degree of parallelism must be greater than zero.");
            }

            if (options.VisibilityTimeout <= TimeSpan.Zero)
            {
                throw new InvalidOperationException("Visibility timeout must be greater than zero.");
            }

            Queues.Add(options);

            return this;
        }

        public AtomizerOptions AddHandlersFrom(params Assembly[] assemblies)
        {
            if (assemblies.Length == 0)
            {
                throw new ArgumentException("At least one assembly must be specified.", nameof(assemblies));
            }

            foreach (var assembly in assemblies)
            {
                var types = assembly
                    .GetTypes()
                    .Where(t => !t.IsAbstract && !t.IsInterface && !t.IsGenericTypeDefinition);

                foreach (var impl in types)
                {
                    var handlerInterfaces = impl.GetInterfaces()
                        .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IAtomizerJobHandler<>));

                    foreach (var handlerInterface in handlerInterfaces)
                    {
                        Handlers.Add(new ServiceDescriptor(handlerInterface, impl, ServiceLifetime.Scoped));
                    }
                }
            }

            return this;
        }

        public AtomizerOptions AddHandlersFrom<TMarker>() => AddHandlersFrom(typeof(TMarker).Assembly);
    }
}
