using Atomizer.Abstractions;
using Atomizer.Dashboard.Abstractions;
using Atomizer.Dashboard.Models;

namespace Atomizer.Dashboard.Services;

/// <summary>
/// Adapts any <see cref="IAtomizerDashboardStorage"/> implementation to the
/// <see cref="IDashboardStorage"/> interface used by the Atomizer Dashboard.
/// </summary>
/// <remarks>
/// <para>
/// The built-in InMemory and EF Core storage backends both implement
/// <see cref="IAtomizerDashboardStorage"/> which provides queue stats, recent jobs, and
/// schedule data. This adapter adds the filtered-query methods
/// (<see cref="GetJobsAsync(JobBrowserFilter, int, int, CancellationToken)"/>
/// and <see cref="CountJobsAsync"/>) that the job-browser view requires.
/// </para>
/// <para>
/// <see cref="GetJobsAsync(JobBrowserFilter, int, int, CancellationToken)"/> and
/// <see cref="CountJobsAsync"/> fetch all jobs from the
/// underlying storage once per call and apply filtering in memory. This is intentional:
/// the InMemory backend retains only a bounded set of terminal jobs, keeping the result
/// set small. For high-throughput production deployments the host application may register
/// its own <see cref="IDashboardStorage"/> implementation with optimised queries.
/// </para>
/// </remarks>
internal sealed class DashboardStorageAdapter : IDashboardStorage
{
    private readonly IAtomizerDashboardStorage _inner;

    /// <summary>
    /// Initialises a new <see cref="DashboardStorageAdapter"/> wrapping
    /// <paramref name="inner"/>.
    /// </summary>
    /// <param name="inner">The core dashboard storage implementation to wrap.</param>
    public DashboardStorageAdapter(IAtomizerDashboardStorage inner)
    {
        _inner = inner;
    }

    // ── IDashboardStorage — filtered query methods ────────────────────────────

    /// <inheritdoc />
    public async Task<IReadOnlyList<AtomizerJob>> GetJobsAsync(
        JobBrowserFilter filter,
        int skip,
        int take,
        CancellationToken cancellationToken
    )
    {
        var all = await _inner.GetRecentJobsAsync(0, int.MaxValue, cancellationToken);

        return all.Where(j => MatchesFilter(j, filter)).Skip(skip).Take(take).ToList();
    }

    /// <inheritdoc />
    public async Task<int> CountJobsAsync(JobBrowserFilter filter, CancellationToken cancellationToken)
    {
        var all = await _inner.GetRecentJobsAsync(0, int.MaxValue, cancellationToken);

        return all.Count(j => MatchesFilter(j, filter));
    }

    // ── IAtomizerDashboardStorage delegations ─────────────────────────────────

    /// <inheritdoc />
    public Task<IReadOnlyList<QueueStats>> GetQueueStatsAsync(CancellationToken cancellationToken) =>
        _inner.GetQueueStatsAsync(cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<AtomizerJob>> GetRecentJobsAsync(
        int skip,
        int take,
        CancellationToken cancellationToken
    ) => _inner.GetRecentJobsAsync(skip, take, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<AtomizerSchedule>> GetAllSchedulesAsync(CancellationToken cancellationToken) =>
        _inner.GetAllSchedulesAsync(cancellationToken);

    /// <inheritdoc />
    public Task<AtomizerJob?> GetJobByIdAsync(Guid jobId, CancellationToken cancellationToken) =>
        _inner.GetJobByIdAsync(jobId, cancellationToken);

    /// <inheritdoc />
    public Task<AtomizerJob?> GetLastJobForScheduleAsync(JobKey jobKey, CancellationToken cancellationToken) =>
        _inner.GetLastJobForScheduleAsync(jobKey, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<ScheduleRecord>> GetSchedulesAsync(CancellationToken cancellationToken) =>
        _inner.GetSchedulesAsync(cancellationToken);

    /// <inheritdoc />
    public Task<AtomizerStats> GetStatsAsync(CancellationToken cancellationToken) =>
        _inner.GetStatsAsync(cancellationToken);

    // ── IAtomizerStorage delegations ──────────────────────────────────────────

    /// <inheritdoc />
    public Task<Guid> InsertAsync(AtomizerJob job, CancellationToken cancellationToken) =>
        _inner.InsertAsync(job, cancellationToken);

    /// <inheritdoc />
    public Task UpdateJobsAsync(IEnumerable<AtomizerJob> jobs, CancellationToken cancellationToken) =>
        _inner.UpdateJobsAsync(jobs, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<AtomizerJob>> GetDueJobsAsync(
        QueueKey queueKey,
        DateTimeOffset now,
        int batchSize,
        CancellationToken cancellationToken
    ) => _inner.GetDueJobsAsync(queueKey, now, batchSize, cancellationToken);

    /// <inheritdoc />
    public Task<int> ReleaseLeasedAsync(
        LeaseToken leaseToken,
        DateTimeOffset now,
        CancellationToken cancellationToken
    ) => _inner.ReleaseLeasedAsync(leaseToken, now, cancellationToken);

    /// <inheritdoc />
    public Task<Guid> UpsertScheduleAsync(AtomizerSchedule schedule, CancellationToken cancellationToken) =>
        _inner.UpsertScheduleAsync(schedule, cancellationToken);

    /// <inheritdoc />
    public Task UpdateSchedulesAsync(IEnumerable<AtomizerSchedule> schedules, CancellationToken cancellationToken) =>
        _inner.UpdateSchedulesAsync(schedules, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<AtomizerSchedule>> GetDueSchedulesAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken
    ) => _inner.GetDueSchedulesAsync(now, cancellationToken);

    /// <inheritdoc />
    public Task<IReadOnlyList<AtomizerJob>> GetJobsAsync(
        JobFilter filter,
        int skip,
        int take,
        CancellationToken cancellationToken
    ) => _inner.GetJobsAsync(filter, skip, take, cancellationToken);

    /// <inheritdoc />
    public Task<AtomizerJob?> GetJobAsync(Guid jobId, CancellationToken cancellationToken) =>
        _inner.GetJobAsync(jobId, cancellationToken);

    /// <inheritdoc />
    public async Task<Guid> RetryJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var original = await _inner.GetJobByIdAsync(jobId, cancellationToken);

        if (original is null)
            throw new InvalidOperationException($"Job {jobId} was not found.");

        if (original.Status != AtomizerJobStatus.Failed)
            throw new InvalidOperationException(
                $"Job {jobId} is in '{original.Status}' status and cannot be retried. "
                    + "Only Failed jobs may be retried from the dashboard."
            );

        if (original.PayloadType is null)
            throw new InvalidOperationException($"Job {jobId} has no payload type and cannot be re-enqueued.");

        var now = DateTimeOffset.UtcNow;

        var retryJob = AtomizerJob.Create(
            original.QueueKey,
            original.PayloadType,
            original.Payload,
            now,
            now,
            original.RetryStrategy,
            idempotencyKey: null, // intentionally cleared — a retry must always be inserted
            scheduleJobKey: original.ScheduleJobKey
        );

        return await _inner.InsertAsync(retryJob, cancellationToken);
    }

    /// <inheritdoc />
    public async Task<bool> CancelJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        var job = await _inner.GetJobByIdAsync(jobId, cancellationToken);

        if (job is null)
            return false;

        if (job.Status != AtomizerJobStatus.Pending)
            return false;

        job.Cancel(DateTimeOffset.UtcNow);

        await _inner.UpdateJobsAsync([job], cancellationToken);

        return true;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns <see langword="true"/> when the given <paramref name="job"/> satisfies
    /// all criteria in <paramref name="filter"/>; <see langword="false"/> otherwise.
    /// </summary>
    private static bool MatchesFilter(AtomizerJob job, JobBrowserFilter filter)
    {
        if (
            filter.QueueName is not null
            && !job.QueueKey.Key.Contains(filter.QueueName, StringComparison.OrdinalIgnoreCase)
        )
            return false;

        if (filter.Status is not null && job.Status != filter.Status)
            return false;

        return true;
    }
}
