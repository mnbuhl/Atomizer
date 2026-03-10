using Atomizer.Dashboard.Abstractions;
using Microsoft.Extensions.Logging;

namespace Atomizer.Dashboard.Services;

/// <summary>
/// Provides a dashboard-safe wrapper around <see cref="IDashboardStorage.CancelJobAsync"/>
/// with structured error handling and logging so that callers (e.g. Blazor components)
/// do not need to manage exceptions themselves.
/// </summary>
/// <remarks>
/// Register this service as <c>Scoped</c> in the DI container alongside the other
/// dashboard services via <see cref="ServiceCollectionExtensions.AddAtomizerDashboard"/>.
/// </remarks>
public sealed class CancelJobService
{
    private readonly IDashboardStorage _storage;
    private readonly ILogger<CancelJobService> _logger;

    /// <summary>
    /// Initialises a new instance of <see cref="CancelJobService"/>.
    /// </summary>
    /// <param name="storage">
    /// The dashboard storage implementation used to look up and cancel pending jobs.
    /// </param>
    /// <param name="logger">Logger for diagnostic output on storage errors.</param>
    public CancelJobService(IDashboardStorage storage, ILogger<CancelJobService> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    /// <summary>
    /// Attempts to cancel the pending job identified by <paramref name="jobId"/>.
    /// </summary>
    /// <remarks>
    /// Returns <see langword="false"/> when the job does not exist or is no longer in
    /// the <see cref="AtomizerJobStatus.Pending"/> state (e.g. it was already picked up
    /// by a worker between the UI refresh and the cancel request).
    /// Storage exceptions are caught, logged at <c>Error</c> level, and translated to a
    /// <see langword="false"/> return value so that the caller can display a friendly
    /// message without propagating raw exceptions into the Blazor UI.
    /// </remarks>
    /// <param name="jobId">The unique identifier of the job to cancel.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>
    /// <see langword="true"/> when the job was found in the Pending state and was
    /// successfully marked as <see cref="AtomizerJobStatus.Cancelled"/>;
    /// <see langword="false"/> otherwise.
    /// </returns>
    public async Task<bool> CancelAsync(Guid jobId, CancellationToken cancellationToken = default)
    {
        try
        {
            var cancelled = await _storage.CancelJobAsync(jobId, cancellationToken);

            if (cancelled)
            {
                _logger.LogInformation("Job {JobId} was cancelled via the dashboard", jobId);
            }
            else
            {
                _logger.LogWarning(
                    "Job {JobId} could not be cancelled — it may no longer be in the Pending state",
                    jobId
                );
            }

            return cancelled;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to cancel job {JobId}", jobId);
            return false;
        }
    }
}
