using System;

namespace Atomizer.Abstractions
{
    public interface IAtomizerServiceResolver
    {
        object Resolve(Type type);

        IAtomizerServiceScope CreateScope();
    }

    public interface IAtomizerServiceScope : IDisposable
    {
        object Resolve(Type type);
    }
}
