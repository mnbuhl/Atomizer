// ReSharper disable once CheckNamespace
namespace Atomizer;

/// <summary>
/// Defines the contract for a job handler that processes payloads of type <typeparamref name="TPayload"/>.
/// </summary>
/// <typeparam name="TPayload">The type of the payload this handler processes.</typeparam>
public interface IAtomizerJob<in TPayload>
{
    /// <summary>
    /// Handles the job with the specified payload.
    /// </summary>
    /// <param name="payload">The deserialized job payload.</param>
    /// <param name="context">The context for this job execution, including the job and a cancellation token.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task HandleAsync(TPayload payload, JobContext context);
}

/// <summary>
/// Provides contextual information for a job being processed.
/// </summary>
public sealed class JobContext
{
    /// <summary>
    /// The job that is being processed.
    /// </summary>
    public AtomizerJob Job { get; set; } = null!;

    /// <summary>
    /// Cancellation token to cancel the job processing.
    /// </summary>
    public CancellationToken CancellationToken { get; set; } = CancellationToken.None;
}
