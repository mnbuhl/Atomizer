namespace Atomizer.Abstractions;

/// <summary>
/// Extends <see cref="IAtomizerStorage"/> with read-only query methods required by the
/// Atomizer Dashboard to display queue statistics, job listings, and recurring-schedule
/// information. Both built-in storage backends (InMemory and EF Core) implement this
/// interface; custom backends that want to support the dashboard should do the same.
/// </summary>
public interface IAtomizerDashboardStorage : IAtomizerStorage
{
    /// <summary>
    /// Returns aggregated queue statistics — pending, processing, completed, and failed
    /// job counts — grouped by <see cref="QueueKey"/>.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>
    /// A read-only list of <see cref="QueueStats"/> objects, one per distinct queue that
    /// holds at least one job, ordered alphabetically by queue name.
    /// </returns>
    Task<IReadOnlyList<QueueStats>> GetQueueStatsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Returns a page of jobs from across all queues, ordered by creation time descending.
    /// </summary>
    /// <param name="skip">Number of jobs to skip (zero-based offset for pagination).</param>
    /// <param name="take">Maximum number of jobs to return.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>A read-only list of <see cref="AtomizerJob"/> objects.</returns>
    Task<IReadOnlyList<AtomizerJob>> GetRecentJobsAsync(int skip, int take, CancellationToken cancellationToken);

    /// <summary>
    /// Returns all recurring schedules stored in the backend, regardless of whether they
    /// are enabled or currently due to run.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>
    /// A read-only list of <see cref="AtomizerSchedule"/> objects ordered by
    /// <see cref="AtomizerSchedule.JobKey"/> ascending.
    /// </returns>
    Task<IReadOnlyList<AtomizerSchedule>> GetAllSchedulesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves a single job by its unique identifier.
    /// Returns <see langword="null"/> when no job with the given
    /// <paramref name="jobId"/> exists in the store.
    /// </summary>
    /// <param name="jobId">The unique identifier of the job to look up.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>
    /// The matching <see cref="AtomizerJob"/>, including its full
    /// <see cref="AtomizerJob.Errors"/> history, or <see langword="null"/> if
    /// not found.
    /// </returns>
    Task<AtomizerJob?> GetJobByIdAsync(Guid jobId, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves the most recently updated <see cref="AtomizerJob"/> that was enqueued by the
    /// specified recurring schedule, or <see langword="null"/> if no such job exists yet.
    /// This is used by the dashboard to surface the last run status for each schedule.
    /// </summary>
    /// <param name="jobKey">
    /// The <see cref="JobKey"/> identifying the recurring schedule. Matched against
    /// <see cref="AtomizerJob.ScheduleJobKey"/>.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>
    /// The most recently updated <see cref="AtomizerJob"/> whose
    /// <see cref="AtomizerJob.ScheduleJobKey"/> equals <paramref name="jobKey"/>,
    /// or <see langword="null"/> if no job has been produced by this schedule yet.
    /// </returns>
    Task<AtomizerJob?> GetLastJobForScheduleAsync(JobKey jobKey, CancellationToken cancellationToken);

    /// <summary>
    /// Returns all recurring schedules stored in the backend as flat, serialisation-friendly
    /// <see cref="ScheduleRecord"/> projections, ordered by job key ascending.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="GetAllSchedulesAsync"/> which returns the full domain object
    /// (<see cref="AtomizerSchedule"/>), this method projects each schedule into a
    /// <see cref="ScheduleRecord"/> that uses primitive and string representations
    /// (e.g. <see cref="ScheduleRecord.PayloadTypeName"/> instead of
    /// <see cref="System.Type"/>, <see cref="ScheduleRecord.TimeZoneId"/> instead of
    /// <see cref="System.TimeZoneInfo"/>). This makes the results directly usable by
    /// the dashboard without requiring runtime type resolution.
    /// </remarks>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>
    /// A read-only list of <see cref="ScheduleRecord"/> objects ordered by
    /// <see cref="ScheduleRecord.JobKey"/> ascending. Returns an empty list when no
    /// schedules have been registered.
    /// </returns>
    Task<IReadOnlyList<ScheduleRecord>> GetSchedulesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Returns a point-in-time <see cref="AtomizerStats"/> snapshot that aggregates
    /// queue depths, job-status totals, error counts, and recurring-schedule summaries
    /// across the entire storage backend.
    /// </summary>
    /// <remarks>
    /// Successive calls can be compared to derive processing-rate metrics: divide the
    /// delta of <see cref="AtomizerStats.TotalCompleted"/> by the elapsed time between
    /// two <see cref="AtomizerStats.GeneratedAt"/> timestamps to get throughput in
    /// jobs per second.
    /// </remarks>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>
    /// An <see cref="AtomizerStats"/> instance populated from the current state of storage.
    /// </returns>
    Task<AtomizerStats> GetStatsAsync(CancellationToken cancellationToken);
}
