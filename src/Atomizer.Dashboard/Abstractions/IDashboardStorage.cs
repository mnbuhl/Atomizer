using Atomizer;
using Atomizer.Abstractions;
using Atomizer.Dashboard.Models;

namespace Atomizer.Dashboard.Abstractions;

/// <summary>
/// Full dashboard storage contract. Extends <see cref="IAtomizerDashboardStorage"/> with
/// additional filtered-query methods required by the dashboard job-browser view.
/// Register a concrete implementation (e.g. <c>DashboardStorageAdapter</c>) in the DI
/// container for <see cref="IDashboardStorage"/> when setting up the dashboard.
/// </summary>
public interface IDashboardStorage : IAtomizerDashboardStorage
{
    /// <summary>
    /// Returns a filtered, paginated page of jobs ordered by creation time descending.
    /// </summary>
    /// <param name="filter">
    /// Optional filter criteria. Pass an empty <see cref="JobBrowserFilter"/> or a filter
    /// with all properties <see langword="null"/> to retrieve all jobs.
    /// </param>
    /// <param name="skip">Number of matching jobs to skip (zero-based offset for pagination).</param>
    /// <param name="take">Maximum number of matching jobs to return per page.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>
    /// A read-only list of <see cref="AtomizerJob"/> objects that match the supplied filter.
    /// </returns>
    Task<IReadOnlyList<AtomizerJob>> GetJobsAsync(
        JobBrowserFilter filter,
        int skip,
        int take,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Returns the total number of jobs that match the supplied filter criteria.
    /// Used by the dashboard to render accurate pagination controls.
    /// </summary>
    /// <param name="filter">
    /// Optional filter criteria. Pass an empty <see cref="JobBrowserFilter"/> to count all jobs.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>The total count of matching jobs.</returns>
    Task<int> CountJobsAsync(JobBrowserFilter filter, CancellationToken cancellationToken);

    /// <summary>
    /// Re-enqueues a failed job as a brand-new <see cref="AtomizerJobStatus.Pending"/> job,
    /// preserving the original queue, payload, payload type, retry strategy, and schedule
    /// association. The new job is assigned a fresh <see cref="System.Guid"/> and its attempt
    /// counter is reset to zero.
    /// </summary>
    /// <param name="jobId">The identifier of the failed job to retry.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>The identifier of the newly created pending job.</returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no job with <paramref name="jobId"/> exists, or when the job is not in
    /// <see cref="AtomizerJobStatus.Failed"/> status.
    /// </exception>
    Task<Guid> RetryJobAsync(Guid jobId, CancellationToken cancellationToken);

    /// <summary>
    /// Cancels a <see cref="AtomizerJobStatus.Pending"/> job, preventing it from
    /// being picked up for processing.
    /// </summary>
    /// <remarks>
    /// Only jobs currently in the <see cref="AtomizerJobStatus.Pending"/> state can be
    /// cancelled. Jobs that are already processing, completed, failed, or cancelled are
    /// not affected and the method returns <see langword="false"/> for them.
    /// </remarks>
    /// <param name="jobId">The unique identifier of the job to cancel.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>
    /// <see langword="true"/> when the job was found and successfully cancelled;
    /// <see langword="false"/> when the job does not exist or is not in the
    /// <see cref="AtomizerJobStatus.Pending"/> state.
    /// </returns>
    Task<bool> CancelJobAsync(Guid jobId, CancellationToken cancellationToken);
}
