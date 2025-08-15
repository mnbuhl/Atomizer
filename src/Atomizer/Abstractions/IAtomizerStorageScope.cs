using System;

namespace Atomizer.Abstractions
{
    public interface IAtomizerStorageScopeFactory
    {
        IAtomizerStorageScope CreateScope();
    }

    public interface IAtomizerStorageScope : IDisposable
    {
        IAtomizerJobStorage Storage { get; }
    }
}
