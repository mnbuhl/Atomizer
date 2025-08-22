using System;
using System.Collections.Concurrent;
using Atomizer.Exceptions;

namespace Atomizer.Hosting
{
    public interface IAtomizerJobTypeResolver
    {
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

            return _cache.GetOrAdd(
                payloadType.AssemblyQualifiedName,
                typeof(IAtomizerJob<>).MakeGenericType(payloadType)
            );
        }
    }
}
