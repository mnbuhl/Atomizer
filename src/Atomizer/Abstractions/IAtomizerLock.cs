namespace Atomizer.Abstractions;

public interface IAtomizerLock : IDisposable
#if NETCOREAPP3_0_OR_GREATER
        , IAsyncDisposable
#endif
{
    /// <summary>
    /// Acquires the lock asynchronously.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task AcquireAsync(CancellationToken cancellationToken);

    bool Acquired { get; }
}
