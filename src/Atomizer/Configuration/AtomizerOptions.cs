using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;

namespace Atomizer
{
    public sealed class AtomizerOptions
    {
        public JobStorageOptions? JobStorageOptions { get; set; }

        internal SchedulingOptions SchedulingOptions { get; set; } = new SchedulingOptions();

        internal List<QueueOptions> Queues { get; } = new List<QueueOptions>();
        internal List<ServiceDescriptor> Handlers { get; } = new List<ServiceDescriptor>();

        public AtomizerOptions AddQueue(string name, Action<QueueOptions>? configure = null)
        {
            var options = new QueueOptions(name);
            configure?.Invoke(options);

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
                    .Where(t => !t.IsAbstract && t is { IsInterface: false, IsGenericTypeDefinition: false });

                foreach (var impl in types)
                {
                    var handlerInterfaces = impl.GetInterfaces()
                        .Where(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IAtomizerJob<>));

                    foreach (var handlerInterface in handlerInterfaces)
                    {
                        Handlers.Add(new ServiceDescriptor(handlerInterface, impl, ServiceLifetime.Scoped));
                    }
                }
            }

            return this;
        }

        public AtomizerOptions AddHandlersFrom<TMarker>() => AddHandlersFrom(typeof(TMarker).Assembly);

        public AtomizerOptions ConfigureScheduling(Action<SchedulingOptions> configure)
        {
            configure.Invoke(SchedulingOptions);

            if (SchedulingOptions.ScheduleLeadTime != null && SchedulingOptions.ScheduleLeadTime <= TimeSpan.Zero)
            {
                throw new InvalidOperationException("Schedule lead time must be greater than zero.");
            }

            if (SchedulingOptions.StorageCheckInterval <= TimeSpan.Zero)
            {
                throw new InvalidOperationException("Storage check interval must be greater than zero.");
            }

            if (SchedulingOptions.VisibilityTimeout <= TimeSpan.Zero)
            {
                throw new InvalidOperationException("Visibility timeout must be greater than zero.");
            }

            return this;
        }
    }
}
