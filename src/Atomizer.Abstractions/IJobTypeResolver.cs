using System;

namespace Atomizer.Abstractions
{
    public interface IJobTypeResolver
    {
        (Type jobType, Type payloadType) Resolve(string jobTypeName, string? fifoKey = null);
    }
}
