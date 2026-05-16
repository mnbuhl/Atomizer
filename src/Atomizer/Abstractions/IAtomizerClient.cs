// ReSharper disable once CheckNamespace
namespace Atomizer;

/// <summary>
/// Provides the public API for enqueuing and scheduling Atomizer jobs.
/// </summary>
public interface IAtomizerClient
{
    /// <summary>
    /// Enqueues a job with the specified payload for immediate processing.
    /// </summary>
    /// <typeparam name="TPayload">The type of the payload to enqueue.</typeparam>
    /// <param name="payload">The payload to enqueue.</param>
    /// <param name="configure">Optional delegate to configure enqueue options such as queue, idempotency key, and retry strategy.</param>
    /// <param name="cancellation">Cancellation token to cancel the operation.</param>
    /// <returns>The unique identifier of the enqueued job.</returns>
    Task<Guid> EnqueueAsync<TPayload>(
        TPayload payload,
        Action<EnqueueOptions>? configure = null,
        CancellationToken cancellation = default
    );

    /// <summary>
    /// Enqueues a job with the specified payload to be processed at the given time.
    /// </summary>
    /// <typeparam name="TPayload">The type of the payload to schedule.</typeparam>
    /// <param name="payload">The payload to schedule.</param>
    /// <param name="runAt">The earliest UTC time at which the job may be processed.</param>
    /// <param name="configure">Optional delegate to configure enqueue options such as queue, idempotency key, and retry strategy.</param>
    /// <param name="cancellation">Cancellation token to cancel the operation.</param>
    /// <returns>The unique identifier of the scheduled job.</returns>
    Task<Guid> ScheduleAsync<TPayload>(
        TPayload payload,
        DateTimeOffset runAt,
        Action<EnqueueOptions>? configure = null,
        CancellationToken cancellation = default
    );

    /// <summary>
    /// Upserts a recurring schedule that enqueues a job on the specified cron expression.
    /// </summary>
    /// <typeparam name="TPayload">The type of the payload the recurring job carries.</typeparam>
    /// <param name="payload">The payload template for each recurring job occurrence.</param>
    /// <param name="name">The unique key identifying this recurring schedule.</param>
    /// <param name="schedule">The cron-based schedule defining when occurrences run.</param>
    /// <param name="configure">Optional delegate to configure recurring options such as misfire policy and timezone.</param>
    /// <param name="cancellation">Cancellation token to cancel the operation.</param>
    /// <returns>The unique identifier of the upserted schedule.</returns>
    Task<Guid> ScheduleRecurringAsync<TPayload>(
        TPayload payload,
        JobKey name,
        Schedule schedule,
        Action<RecurringOptions>? configure = null,
        CancellationToken cancellation = default
    );

    /// <summary>
    /// Dequeues a pending job so it will not be processed.
    /// </summary>
    /// <param name="jobId">The unique identifier of the job to dequeue.</param>
    /// <param name="cancellation">Cancellation token to cancel the operation.</param>
    /// <returns>
    /// <see langword="true"/> when a pending job was dequeued; otherwise <see langword="false"/> when the job
    /// does not exist or is no longer pending.
    /// </returns>
    Task<bool> DequeueAsync(Guid jobId, CancellationToken cancellation = default);

    /// <summary>
    /// Idempotently deletes a recurring schedule.
    /// </summary>
    /// <param name="name">The unique key identifying the recurring schedule.</param>
    /// <param name="cancellation">Cancellation token to cancel the operation.</param>
    /// <returns><see langword="true"/> when a schedule was deleted; otherwise <see langword="false"/>.</returns>
    Task<bool> DeleteRecurringAsync(JobKey name, CancellationToken cancellation = default);

    /// <summary>
    /// Executes a job immediately in the current process while recording it in Atomizer as completed or failed.
    /// </summary>
    /// <typeparam name="TPayload">The type of the payload to execute.</typeparam>
    /// <param name="payload">The payload to execute.</param>
    /// <param name="configure">Optional delegate to configure execution options such as queue, idempotency key, and retry strategy.</param>
    /// <param name="cancellation">Cancellation token to cancel the operation.</param>
    /// <returns>The unique identifier of the executed job.</returns>
    /// <remarks>
    /// If the job handler throws, Atomizer records the job as failed and then rethrows the original exception.
    /// Direct execution does not schedule background retries.
    /// </remarks>
    Task<Guid> ExecuteAsync<TPayload>(
        TPayload payload,
        Action<EnqueueOptions>? configure = null,
        CancellationToken cancellation = default
    );
}

/// <summary>
/// Configures options for a single job enqueue or schedule operation.
/// </summary>
public sealed class EnqueueOptions
{
    /// <summary>
    /// The queue to which the job will be added. Defaults to AtomizerQueue.Default.
    /// </summary>
    public QueueKey Queue { get; set; } = QueueKey.Default;

    /// <summary>
    /// The idempotency key for the job, used to ensure that duplicate jobs are not processed.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>
    /// The retry strategy to apply when a job fails.
    /// <remarks>Defaults to 3 attempts with 15 seconds delays</remarks>
    /// </summary>
    public RetryStrategy RetryStrategy { get; set; } = RetryStrategy.Default;

    /// <summary>
    /// The partition key used to enforce ordered (FIFO) processing within this queue.
    /// </summary>
    /// <remarks>
    /// When set, jobs sharing the same <see cref="PartitionKey"/> and queue are processed one at a time
    /// in sequence-number order. Defaults to <see langword="null"/>, meaning the job participates in no partition.
    /// </remarks>
    public PartitionKey? PartitionKey { get; set; }
}

/// <summary>
/// Configures options for a recurring schedule registration.
/// </summary>
public sealed class RecurringOptions
{
    /// <summary>
    /// The queue to which the job will be added. Defaults to AtomizerQueue.Default.
    /// </summary>
    public QueueKey Queue { get; set; } = QueueKey.Default;

    /// <summary>
    /// The retry strategy to apply when a job fails.
    /// <remarks>Defaults to 3 attempts with 15 seconds delays</remarks>
    /// </summary>
    public RetryStrategy RetryStrategy { get; set; } = RetryStrategy.Default;

    /// <summary>
    /// The policy to apply when a job misfires.
    /// <remarks>Defaults to MisfirePolicy.ExecuteNow.</remarks>
    /// </summary>
    public MisfirePolicy MisfirePolicy { get; set; } = MisfirePolicy.ExecuteNow;

    /// <summary>
    /// The maximum number of missed runs to catch up on when the job is re-enabled.
    /// <remarks>Defaults to 5 missed runs. Only evaluated when MisfirePolicy is set to CatchUp.</remarks>
    /// </summary>
    public int MaxCatchUp { get; set; } = 5;

    /// <summary>
    /// The time zone in which the cron expression should be evaluated.
    /// <remarks>Defaults to UTC.</remarks>
    /// </summary>
    public TimeZoneInfo TimeZone { get; set; } = TimeZoneInfo.Utc;

    /// <summary>
    /// Whether the recurring job is enabled.
    /// <remarks>Defaults to true.</remarks>
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The partition key used to enforce ordered (FIFO) processing of recurring job occurrences.
    /// </summary>
    /// <remarks>
    /// When set, each occurrence enqueued from this schedule carries the same <see cref="PartitionKey"/>,
    /// preventing overlapping occurrences of the same recurring job from processing concurrently.
    /// Defaults to <see langword="null"/>, meaning occurrences participate in no partition.
    /// </remarks>
    public PartitionKey? PartitionKey { get; set; }
}
