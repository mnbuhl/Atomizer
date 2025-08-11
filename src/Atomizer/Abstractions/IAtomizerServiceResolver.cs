using System;

namespace Atomizer.Abstractions
{
    public interface IAtomizerServiceResolver
    {
        object Resolve(Type type);
        TService Resolve<TService>()
            where TService : notnull;

        IAtomizerServiceScope CreateScope();
    }

    public interface IAtomizerServiceScope : IDisposable
    {
        object Resolve(Type type);
        TService Resolve<TService>()
            where TService : notnull;
    }
}
