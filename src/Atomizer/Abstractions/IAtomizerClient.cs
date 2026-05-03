// ReSharper disable once CheckNamespace
namespace Atomizer;

/// <summary>
/// Provides the public API for enqueuing and scheduling jobs.
/// </summary>
public interface IAtomizerClient
{
    /// <summary>
    /// Enqueues a job for immediate processing.
    /// </summary>
    /// <typeparam name="TPayload">The type of the job payload.</typeparam>
    /// <param name="payload">The payload to pass to the job handler.</param>
    /// <param name="configure">Optional delegate to configure enqueue options.</param>
    /// <param name="cancellation">Cancellation token to cancel the operation.</param>
    /// <returns>The unique identifier of the enqueued job.</returns>
    Task<Guid> EnqueueAsync<TPayload>(
        TPayload payload,
        Action<EnqueueOptions>? configure = null,
        CancellationToken cancellation = default
    );

    /// <summary>
    /// Schedules a job to run at the specified time.
    /// </summary>
    /// <typeparam name="TPayload">The type of the job payload.</typeparam>
    /// <param name="payload">The payload to pass to the job handler.</param>
    /// <param name="runAt">The UTC time at which the job should become visible for processing.</param>
    /// <param name="configure">Optional delegate to configure enqueue options.</param>
    /// <param name="cancellation">Cancellation token to cancel the operation.</param>
    /// <returns>The unique identifier of the scheduled job.</returns>
    Task<Guid> ScheduleAsync<TPayload>(
        TPayload payload,
        DateTimeOffset runAt,
        Action<EnqueueOptions>? configure = null,
        CancellationToken cancellation = default
    );

    /// <summary>
    /// Upserts a recurring job schedule identified by the given key.
    /// </summary>
    /// <typeparam name="TPayload">The type of the job payload.</typeparam>
    /// <param name="payload">The payload to pass to the job handler on each occurrence.</param>
    /// <param name="name">The unique key that identifies this recurring schedule.</param>
    /// <param name="schedule">The cron-based schedule defining when the job runs.</param>
    /// <param name="configure">Optional delegate to configure recurring options.</param>
    /// <param name="cancellation">Cancellation token to cancel the operation.</param>
    /// <returns>The unique identifier of the upserted schedule.</returns>
    Task<Guid> ScheduleRecurringAsync<TPayload>(
        TPayload payload,
        JobKey name,
        Schedule schedule,
        Action<RecurringOptions>? configure = null,
        CancellationToken cancellation = default
    );
}

/// <summary>
/// Options for enqueuing or scheduling a single job.
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
}

/// <summary>
/// Options for configuring a recurring job schedule.
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
}
