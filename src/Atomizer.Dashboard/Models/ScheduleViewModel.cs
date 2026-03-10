namespace Atomizer.Dashboard.Models;

/// <summary>
/// A read-model projection used by the Atomizer dashboard recurring-schedules view.
/// Combines data from an <see cref="AtomizerSchedule"/> record with the outcome of
/// the most recently associated <see cref="AtomizerJob"/> so that a single row of
/// the schedules table contains next-run time <em>and</em> last-run status without
/// requiring the Blazor component to perform multiple storage calls itself.
/// </summary>
public sealed class ScheduleViewModel
{
    /// <summary>Gets the unique identifier of the schedule record.</summary>
    public Guid Id { get; init; }

    /// <summary>
    /// Gets the job key that uniquely identifies this recurring schedule within the system.
    /// </summary>
    public string JobKey { get; init; } = string.Empty;

    /// <summary>
    /// Gets the key of the queue onto which jobs produced by this schedule are enqueued.
    /// </summary>
    public string QueueKey { get; init; } = string.Empty;

    /// <summary>
    /// Gets the six-part (seconds-level) cron expression that defines the schedule cadence.
    /// </summary>
    public string CronExpression { get; init; } = string.Empty;

    /// <summary>
    /// Gets the UTC instant at which this schedule is next due to fire.
    /// <see cref="DateTimeOffset.MaxValue"/> indicates no further occurrences.
    /// </summary>
    public DateTimeOffset NextRunAt { get; init; }

    /// <summary>
    /// Gets the UTC instant at which a job was most recently enqueued for this schedule,
    /// or <see langword="null"/> if the schedule has never triggered.
    /// </summary>
    public DateTimeOffset? LastRunAt { get; init; }

    /// <summary>
    /// Gets the <see cref="AtomizerJobStatus"/> of the most recently completed or failed
    /// job associated with this schedule, or <see langword="null"/> if no job has run yet.
    /// </summary>
    public AtomizerJobStatus? LastRunStatus { get; init; }

    /// <summary>
    /// Gets a value indicating whether this schedule is currently active. When
    /// <see langword="false"/> the scheduler will not enqueue new jobs for this schedule.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>Gets the misfire policy applied when a scheduled run is missed.</summary>
    public MisfirePolicy MisfirePolicy { get; init; }

    /// <summary>
    /// Gets the fully qualified name of the payload type handled by this schedule,
    /// or <see langword="null"/> if the type cannot be resolved.
    /// </summary>
    public string? PayloadTypeName { get; init; }
}
