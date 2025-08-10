using System;

namespace Atomizer.Abstractions
{
    public interface IAtomizerJobTypeResolver
    {
        Type Resolve(Type payloadType);
    }
}
