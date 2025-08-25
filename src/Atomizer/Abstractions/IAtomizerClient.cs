// ReSharper disable once CheckNamespace
namespace Atomizer;

public interface IAtomizerClient
{
    Task<Guid> EnqueueAsync<TPayload>(
        TPayload payload,
        Action<EnqueueOptions>? configure = null,
        CancellationToken cancellation = default
    );

    Task<Guid> ScheduleAsync<TPayload>(
        TPayload payload,
        DateTimeOffset runAt,
        Action<EnqueueOptions>? configure = null,
        CancellationToken cancellation = default
    );

    Task<Guid> ScheduleRecurringAsync<TPayload>(
        TPayload payload,
        JobKey name,
        Schedule schedule,
        Action<RecurringOptions>? configure = null,
        CancellationToken cancellation = default
    );
}

public sealed class EnqueueOptions
{
    /// <summary>
    /// The queue to which the job will be added. Defaults to AtomizerQueue.Default.
    /// </summary>
    public QueueKey Queue { get; set; } = QueueKey.Default;

    /// <summary>
    /// Enqueue the job with a different type than the registered type.
    /// </summary>
    public Type? TypeOverride { get; set; }

    /// <summary>
    /// The idempotency key for the job, used to ensure that duplicate jobs are not processed.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>
    /// The maximum number of attempts to process the job before it is considered failed.
    /// <remarks>Defaults to 3 attempts.</remarks>
    /// </summary>
    public int MaxAttempts { get; set; } = 3;
}

public sealed class RecurringOptions
{
    /// <summary>
    /// The queue to which the job will be added. Defaults to AtomizerQueue.Default.
    /// </summary>
    public QueueKey Queue { get; set; } = QueueKey.Default;

    /// <summary>
    /// The maximum number of attempts to process the job before it is considered failed.
    /// <remarks>Defaults to 3 attempts.</remarks>
    /// </summary>
    public int MaxAttempts { get; set; } = 3;

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
