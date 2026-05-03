// ReSharper disable once CheckNamespace
namespace Atomizer;

/// <summary>
/// Defines the handler for a job with the specified payload type.
/// </summary>
/// <typeparam name="TPayload">The type of the payload this handler processes.</typeparam>
public interface IAtomizerJob<in TPayload>
{
    /// <summary>
    /// Handles the job with the specified payload and context.
    /// </summary>
    /// <param name="payload">The deserialized job payload.</param>
    /// <param name="context">Context providing job metadata and a cancellation token.</param>
    /// <returns>A task representing the asynchronous handler execution.</returns>
    Task HandleAsync(TPayload payload, JobContext context);
}

/// <summary>
/// Provides metadata and cancellation support to a running job handler.
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
