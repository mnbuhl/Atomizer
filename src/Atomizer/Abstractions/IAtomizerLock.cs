namespace Atomizer.Abstractions;

public interface IAtomizerLock : IDisposable
#if NETCOREAPP3_0_OR_GREATER
        , IAsyncDisposable
#endif
{
    /// <summary>
    /// Whether the lock has been successfully acquired.
    /// </summary>
    bool Acquired { get; }

    /// <summary>
    /// The type of the lock.
    /// </summary>
    LockType Mode { get; }
}

public enum LockType
{
    /// <summary>
    /// A lock that is local to the current process.
    /// </summary>
    InMemory,

    /// <summary>
    /// A lock that is distributed across multiple processes.
    /// </summary>
    Distributed,

    /// <summary>
    /// A lock that is tied to a database transaction.
    /// </summary>
    DatabaseTransaction,
}
