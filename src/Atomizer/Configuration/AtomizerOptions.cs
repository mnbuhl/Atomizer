using System.Reflection;
using Atomizer.Exceptions;
using Microsoft.Extensions.DependencyInjection;

namespace Atomizer;

/// <summary>
/// Configures the Atomizer background job system.
/// </summary>
public sealed class AtomizerOptions
{
    /// <summary>
    /// Gets or sets the storage backend options. Must be configured before the host starts.
    /// </summary>
    public JobStorageOptions? JobStorageOptions { get; set; }

    internal SchedulingOptions SchedulingOptions { get; set; } = new SchedulingOptions();

    internal List<QueueOptions> Queues { get; } = new List<QueueOptions>();
    internal List<ServiceDescriptor> Handlers { get; } = new List<ServiceDescriptor>();

    /// <summary>
    /// Adds a named queue with optional configuration.
    /// </summary>
    /// <param name="name">The unique key of the queue. Validation is applied immediately at the call site.</param>
    /// <param name="configure">Optional delegate to configure queue-specific options.</param>
    /// <returns>The current <see cref="AtomizerOptions"/> instance for chaining.</returns>
    public AtomizerOptions AddQueue(QueueKey name, Action<QueueOptions>? configure = null)
    {
        var options = new QueueOptions(name);
        configure?.Invoke(options);

        if (options.BatchSize <= 0)
        {
            throw new InvalidAtomizerConfigurationException("Batch size must be greater than zero.");
        }

        if (options.DegreeOfParallelism <= 0)
        {
            throw new InvalidAtomizerConfigurationException("Degree of parallelism must be greater than zero.");
        }

        if (options.VisibilityTimeout <= TimeSpan.Zero)
        {
            throw new InvalidAtomizerConfigurationException("Visibility timeout must be greater than zero.");
        }

        Queues.Add(options);

        return this;
    }

    /// <summary>
    /// Scans the specified assemblies for <see cref="IAtomizerJob{TPayload}"/> implementations and registers them as scoped services.
    /// </summary>
    /// <param name="assemblies">One or more assemblies to scan.</param>
    /// <returns>The current <see cref="AtomizerOptions"/> instance for chaining.</returns>
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

    /// <summary>
    /// Scans the assembly containing <typeparamref name="TMarker"/> for <see cref="IAtomizerJob{TPayload}"/> implementations.
    /// </summary>
    /// <typeparam name="TMarker">A type whose assembly is scanned for job handlers.</typeparam>
    /// <returns>The current <see cref="AtomizerOptions"/> instance for chaining.</returns>
    public AtomizerOptions AddHandlersFrom<TMarker>() => AddHandlersFrom(typeof(TMarker).Assembly);

    /// <summary>
    /// Configures the scheduling subsystem options.
    /// </summary>
    /// <param name="configure">Delegate to configure <see cref="SchedulingOptions"/>.</param>
    /// <returns>The current <see cref="AtomizerOptions"/> instance for chaining.</returns>
    public AtomizerOptions ConfigureScheduling(Action<SchedulingOptions> configure)
    {
        configure.Invoke(SchedulingOptions);

        if (SchedulingOptions.ScheduleLeadTime != null && SchedulingOptions.ScheduleLeadTime <= TimeSpan.Zero)
        {
            throw new InvalidAtomizerConfigurationException("Schedule lead time must be greater than zero.");
        }

        if (SchedulingOptions.StorageCheckInterval <= TimeSpan.Zero)
        {
            throw new InvalidAtomizerConfigurationException("Storage check interval must be greater than zero.");
        }

        if (SchedulingOptions.VisibilityTimeout <= TimeSpan.Zero)
        {
            throw new InvalidAtomizerConfigurationException("Visibility timeout must be greater than zero.");
        }

        return this;
    }
}
