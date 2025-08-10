using System;
using System.Collections.Concurrent;
using Atomizer.Abstractions;

namespace Atomizer.Hosting
{
    public class DefaultJobTypeResolver : IAtomizerJobTypeResolver
    {
        private readonly ConcurrentDictionary<string, Type> _cache = new ConcurrentDictionary<string, Type>();

        public Type Resolve(Type payloadType)
        {
            return _cache.GetOrAdd(
                payloadType.AssemblyQualifiedName!,
                typeof(IAtomizerJobHandler<>).MakeGenericType(payloadType)
            );
        }
    }
}
