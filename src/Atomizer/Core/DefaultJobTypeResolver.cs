using System.Collections.Concurrent;
using Atomizer.Exceptions;

namespace Atomizer.Core;

/// <summary>
/// Resolves the <see cref="IAtomizerJob{TPayload}"/> handler type for a given payload type.
/// </summary>
public interface IAtomizerJobTypeResolver
{
    /// <summary>
    /// Resolves the handler type for the specified payload type.
    /// </summary>
    /// <param name="payloadType">The payload type to resolve a handler for.</param>
    /// <returns>The <see cref="IAtomizerJob{TPayload}"/> constructed generic type for the given payload.</returns>
    Type Resolve(Type payloadType);
}

internal sealed class DefaultJobTypeResolver : IAtomizerJobTypeResolver
{
    private readonly ConcurrentDictionary<string, Type> _cache = new ConcurrentDictionary<string, Type>();

    public Type Resolve(Type payloadType)
    {
        if (payloadType.AssemblyQualifiedName is null)
        {
            throw new JobResolverException(
                $"Payload type '{payloadType.Name}' does not have an assembly qualified name.",
                payloadType
            );
        }

        return _cache.GetOrAdd(payloadType.AssemblyQualifiedName, typeof(IAtomizerJob<>).MakeGenericType(payloadType));
    }
}
