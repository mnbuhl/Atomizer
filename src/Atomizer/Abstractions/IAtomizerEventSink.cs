namespace Atomizer;

/// <summary>
/// Receives notifications when a job's state changes.
/// Register a custom implementation to react to job lifecycle events.
/// The default implementation is a no-op.
/// </summary>
public interface IAtomizerEventSink
{
    /// <summary>
    /// Called after a job transitions to a new state.
    /// Implementations must be fast and non-blocking; exceptions are caught and logged.
    /// </summary>
    Task OnJobStateChangedAsync(AtomizerJob job, CancellationToken cancellationToken);
}
