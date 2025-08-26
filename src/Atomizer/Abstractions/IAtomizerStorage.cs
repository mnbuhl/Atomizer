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
    /// Updates an existing Atomizer job in the storage.
    /// </summary>
    /// <param name="job">The Atomizer job to be updated.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task UpdateAsync(AtomizerJob job, CancellationToken cancellationToken);

    /// <summary>
    /// Updates a range of existing Atomizer jobs in the storage.
    /// </summary>
    /// <param name="jobs">The collection of Atomizer jobs to be updated.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task UpdateRangeAsync(IEnumerable<AtomizerJob> jobs, CancellationToken cancellationToken);

    /// <summary>
    /// Leases a batch of jobs from the specified queue.
    /// The storage will mark these jobs as "Processing" and set their visibility timeout as well as LeaseToken.
    /// The leased jobs will not be available for leasing by other consumers until the visibility timeout expires
    /// </summary>
    /// <param name="queueKey">The key of the queue from which to lease jobs.</param>
    /// <param name="batchSize">The maximum number of jobs to lease in this batch.</param>
    /// <param name="now">The current date and time in UTC</param>
    /// <param name="visibilityTimeout">The duration for which the leased jobs will be invisible to other consumers.</param>
    /// <param name="leaseToken">The lease token representing the consumer leasing the jobs.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A list of leased Atomizer jobs.</returns>
    Task<IReadOnlyList<AtomizerJob>> LeaseBatchAsync(
        QueueKey queueKey,
        int batchSize,
        DateTimeOffset now,
        TimeSpan visibilityTimeout,
        LeaseToken leaseToken,
        CancellationToken cancellationToken
    );

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
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>The number of jobs released.</returns>
    Task<int> ReleaseLeasedAsync(LeaseToken leaseToken, CancellationToken cancellationToken);

    /// <summary>
    /// Inserts or updates an Atomizer schedule in the storage and returns its unique identifier.
    /// </summary>
    /// <param name="schedule">The Atomizer schedule to be upserted.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>The unique identifier of the upserted schedule.</returns>
    Task<Guid> UpsertScheduleAsync(AtomizerSchedule schedule, CancellationToken cancellationToken);

    /// <summary>
    /// Leases due schedules that are scheduled to run at or before the specified time.
    /// The storage will add the specified visibility timeout to the leased schedules and set their LeaseToken.
    /// The leased schedules will not be available for leasing by other consumers until the visibility timeout expires.
    /// </summary>
    /// <param name="now">The current date and time in UTC.</param>
    /// <param name="visibilityTimeout">The duration for which the leased schedules will be invisible to other consumers.</param>
    /// <param name="leaseToken">The lease token representing the consumer leasing the schedules.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A list of leased Atomizer schedules.</returns>
    Task<IReadOnlyList<AtomizerSchedule>> LeaseDueSchedulesAsync(
        DateTimeOffset now,
        TimeSpan visibilityTimeout,
        LeaseToken leaseToken,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Retrieves schedules that are due for execution at or before the specified time.
    /// </summary>
    /// <param name="now">The current date and time in UTC.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>A list of due Atomizer schedules.</returns>
    Task<IReadOnlyList<AtomizerSchedule>> GetDueSchedulesAsync(DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>
    /// Releases all schedules leased with the specified lease token, making them available for leasing again.
    /// </summary>
    /// <param name="leaseToken">The lease token representing the consumer releasing the schedules.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns>The number of schedules released.</returns>
    Task<int> ReleaseLeasedSchedulesAsync(LeaseToken leaseToken, CancellationToken cancellationToken);

    /// <summary>
    /// Acquires a distributed lock for the specified queue key.
    /// </summary>
    /// <param name="queueKey">The key of the queue for which to acquire the lock.</param>
    /// <param name="lockTimeout">The duration for which the lock will be held before it is automatically released.</param>
    /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
    /// <returns></returns>
    Task<IAtomizerLock> AcquireLockAsync(QueueKey queueKey, TimeSpan lockTimeout, CancellationToken cancellationToken);
}
