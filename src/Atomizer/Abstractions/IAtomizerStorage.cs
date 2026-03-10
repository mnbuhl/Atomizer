namespace Atomizer.Abstractions;

public interface IAtomizerStorage
{
    /// <summary>
    /// Inserts a new Atomizer job into the storage and returns its unique identifier.
    /// </summary>
    /// <param name="job">The Atomizer job to be inserted.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>The unique identifier of the inserted job.</returns>
    Task<Guid> InsertAsync(AtomizerJob job, CancellationToken cancellationToken);

    /// <summary>
    /// Updates a range of existing Atomizer jobs in the storage.
    /// </summary>
    /// <param name="jobs">The collection of Atomizer jobs to be updated.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task UpdateJobsAsync(IEnumerable<AtomizerJob> jobs, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves jobs that are due for processing from the specified queue.
    /// </summary>
    /// <param name="queueKey">The key of the queue from which to retrieve due jobs.</param>
    /// <param name="now">The current date and time in UTC.</param>
    /// <param name="batchSize">The maximum number of jobs to retrieve in this batch.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A list of due Atomizer jobs.</returns>
    Task<IReadOnlyList<AtomizerJob>> GetDueJobsAsync(
        QueueKey queueKey,
        DateTimeOffset now,
        int batchSize,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Releases all jobs leased with the specified lease token, making them available for leasing again.
    /// The storage will mark these jobs as "Pending" and clear their visibility timeout as well as LeaseToken.
    /// </summary>
    /// <param name="leaseToken">The lease token representing the consumer releasing the jobs.</param>
    /// <param name="now">The current date and time in UTC.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>The number of jobs released.</returns>
    Task<int> ReleaseLeasedAsync(LeaseToken leaseToken, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts or updates an Atomizer schedule in the storage and returns its unique identifier.
    /// </summary>
    /// <param name="schedule">The Atomizer schedule to be upserted.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>The unique identifier of the upserted schedule.</returns>
    Task<Guid> UpsertScheduleAsync(AtomizerSchedule schedule, CancellationToken cancellationToken);

    /// <summary>
    /// Updates a range of existing schedules jobs in the storage.
    /// </summary>
    /// <param name="schedules">The collection of Atomizer schedules to be updated.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task UpdateSchedulesAsync(IEnumerable<AtomizerSchedule> schedules, CancellationToken cancellationToken);

    /// <summary>
    /// Retrieves schedules that are due for execution at or before the specified time.
    /// </summary>
    /// <param name="now">The current date and time in UTC.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A list of due Atomizer schedules.</returns>
    Task<IReadOnlyList<AtomizerSchedule>> GetDueSchedulesAsync(DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// Returns a filtered, paginated list of jobs ordered by creation time descending.
    /// </summary>
    /// <remarks>
    /// Multiple non-null properties on <paramref name="filter"/> are combined with a
    /// logical AND — a job must satisfy every active criterion to be included.
    /// Passing a filter whose <see cref="JobFilter.IsEmpty"/> is <see langword="true"/>
    /// is equivalent to returning all jobs subject only to the pagination parameters.
    /// </remarks>
    /// <param name="filter">
    /// Filter criteria to apply. All properties default to <see langword="null"/> which
    /// disables that dimension; construct a <see cref="JobFilter"/> with the desired
    /// properties set to constrain the result.
    /// </param>
    /// <param name="skip">
    /// Number of matching jobs to skip before returning results (zero-based offset for
    /// pagination). Must be non-negative.
    /// </param>
    /// <param name="take">
    /// Maximum number of matching jobs to return per page. Use <see cref="int.MaxValue"/>
    /// to retrieve all matching jobs. Must be non-negative.
    /// </param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>
    /// A read-only list of <see cref="AtomizerJob"/> instances that satisfy
    /// <paramref name="filter"/>, ordered by <see cref="AtomizerJob.CreatedAt"/> descending.
    /// </returns>
    Task<IReadOnlyList<AtomizerJob>> GetJobsAsync(
        JobFilter filter,
        int skip,
        int take,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Retrieves a single job by its unique identifier, including the full error history
    /// recorded against it.
    /// </summary>
    /// <param name="jobId">The unique identifier of the job to retrieve.</param>
    /// <param name="cancellationToken">A token to cancel the asynchronous operation.</param>
    /// <returns>
    /// The <see cref="AtomizerJob"/> with the given <paramref name="jobId"/>, including
    /// its populated <see cref="AtomizerJob.Errors"/> collection, or
    /// <see langword="null"/> when no job with that identifier exists in the store.
    /// </returns>
    Task<AtomizerJob?> GetJobAsync(Guid jobId, CancellationToken cancellationToken);
}
