using System;

namespace Atomizer.Abstractions
{
    public interface IJobTypeResolver
    {
        Type Resolve(Type payloadType);
    }
}
