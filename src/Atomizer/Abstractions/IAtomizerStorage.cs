namespace Atomizer.Abstractions;

/// <summary>
/// Defines the storage contract for persisting and querying Atomizer jobs and schedules.
/// </summary>
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
    /// Inserts or refreshes the current process heartbeat.
    /// </summary>
    Task UpsertHeartbeatAsync(AtomizerActiveServer server, CancellationToken cancellationToken);

    /// <summary>
    /// Returns active server records whose last heartbeat is older than <paramref name="staleBefore"/>.
    /// </summary>
    Task<IReadOnlyList<AtomizerActiveServer>> GetStaleServersAsync(
        DateTimeOffset staleBefore,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Atomically claims a stale server and releases all processing jobs leased by that exact instance.
    /// </summary>
    Task<AtomizerHeartbeatRecoveryResult> TryRecoverStaleServerAsync(
        string instanceId,
        DateTimeOffset staleBefore,
        DateTimeOffset now,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Removes a server heartbeat record when the current process shuts down cleanly.
    /// </summary>
    Task RemoveHeartbeatAsync(string instanceId, CancellationToken cancellationToken);

    /// <summary>
    /// Executes the specified callback within an exclusive lease for the given queue.
    /// The backend acquires its lock or transaction before invoking the callback and
    /// releases or commits it after the callback completes. If the callback throws,
    /// the lease is rolled back or released and the exception is rethrown.
    /// </summary>
    /// <typeparam name="TResult">The type of value returned by the callback.</typeparam>
    /// <param name="queue">The queue key identifying the lease boundary.</param>
    /// <param name="callback">
    /// The work to execute inside the lease. Receives a <see cref="CancellationToken"/>
    /// that is cancelled if the lease expires or the host shuts down.
    /// </param>
    /// <param name="cancellationToken">Cancellation token to cancel the lease acquisition.</param>
    /// <returns>The value returned by <paramref name="callback"/>.</returns>
    Task<TResult> ExecuteInLeaseAsync<TResult>(
        QueueKey queue,
        Func<CancellationToken, Task<TResult>> callback,
        CancellationToken cancellationToken
    );

    /// <summary>
    /// Executes the specified callback within an exclusive lease for the given queue.
    /// The backend acquires its lock or transaction before invoking the callback and
    /// releases or commits it after the callback completes. If the callback throws,
    /// the lease is rolled back or released and the exception is rethrown.
    /// </summary>
    /// <param name="queue">The queue key identifying the lease boundary.</param>
    /// <param name="callback">
    /// The work to execute inside the lease. Receives a <see cref="CancellationToken"/>
    /// that is cancelled if the lease expires or the host shuts down.
    /// </param>
    /// <param name="cancellationToken">Cancellation token to cancel the lease acquisition.</param>
    /// <returns>A task representing the asynchronous operation.</returns>
    Task ExecuteInLeaseAsync(
        QueueKey queue,
        Func<CancellationToken, Task> callback,
        CancellationToken cancellationToken
    );
}
