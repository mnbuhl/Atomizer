using System;
using System.Collections.Concurrent;

namespace Atomizer.Hosting
{
    public interface IAtomizerJobTypeResolver
    {
        Type Resolve(Type payloadType);
    }

    public class DefaultJobTypeResolver : IAtomizerJobTypeResolver
    {
        private readonly ConcurrentDictionary<string, Type> _cache = new ConcurrentDictionary<string, Type>();

        public Type Resolve(Type payloadType)
        {
            return _cache.GetOrAdd(
                payloadType.AssemblyQualifiedName!,
                typeof(IAtomizerJob<>).MakeGenericType(payloadType)
            );
        }
    }
}
