namespace Atomizer.Abstractions;

public interface IAtomizerLeasingScopeFactory
{
    /// <summary>
    /// Creates a leasing scope for the given queue key with the specified lock timeout.
    /// </summary>
    /// <param name="key">Key of the queue to create the scope for.</param>
    /// <param name="scopeTimeout">The maximum duration to keep the scope alive.</param>
    /// <param name="cancellationToken">A cancellation token to cancel the operation.</param>
    /// <returns>Returns the created leasing scope.</returns>
    Task<IAtomizerLeasingScope> CreateScopeAsync(
        QueueKey key,
        TimeSpan scopeTimeout,
        CancellationToken cancellationToken
    );
}
