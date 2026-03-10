namespace Atomizer;

/// <summary>
/// A flat, serialisation-friendly read model representing a single recurring
/// schedule stored in the Atomizer backend. Returned by
/// <see cref="Abstractions.IAtomizerDashboardStorage.GetSchedulesAsync"/> and
/// consumed by the Atomizer Dashboard and any custom monitoring code.
/// </summary>
/// <remarks>
/// Unlike the domain class <see cref="AtomizerSchedule"/>, <see cref="ScheduleRecord"/>
/// contains no behaviour and uses primitive / string representations (e.g.
/// <see cref="PayloadTypeName"/> instead of <see cref="System.Type"/>,
/// <see cref="TimeZoneId"/> instead of <see cref="System.TimeZoneInfo"/>) so that it
/// can be projected directly from storage without resolving runtime types.
/// </remarks>
/// <param name="Id">The unique identifier of the schedule.</param>
/// <param name="JobKey">
/// The string key that uniquely identifies this recurring schedule within the system.
/// </param>
/// <param name="QueueKey">
/// The key of the queue onto which jobs produced by this schedule are enqueued.
/// </param>
/// <param name="CronExpression">
/// The six-part (seconds-level) cron expression that defines the schedule cadence.
/// </param>
/// <param name="Enabled">
/// <see langword="true"/> when the schedule is active and will continue to enqueue jobs;
/// <see langword="false"/> when it has been disabled.
/// </param>
/// <param name="MisfirePolicy">
/// The policy applied when a scheduled run is missed.
/// </param>
/// <param name="NextRunAt">
/// The UTC instant at which this schedule is next due to fire.
/// <see cref="System.DateTimeOffset.MaxValue"/> indicates no further occurrences.
/// </param>
/// <param name="LastEnqueueAt">
/// The UTC instant at which a job was most recently enqueued for this schedule,
/// or <see langword="null"/> if the schedule has never triggered.
/// </param>
/// <param name="CreatedAt">The UTC instant at which the schedule was first persisted.</param>
/// <param name="UpdatedAt">The UTC instant at which the schedule was last modified.</param>
/// <param name="PayloadTypeName">
/// The assembly-qualified or fully-qualified name of the payload type handled by this
/// schedule, or <see langword="null"/> if type information is unavailable.
/// </param>
/// <param name="TimeZoneId">
/// The IANA or Windows time-zone identifier used when computing occurrences.
/// Defaults to <c>"UTC"</c>.
/// </param>
public sealed record ScheduleRecord(
    Guid Id,
    string JobKey,
    string QueueKey,
    string CronExpression,
    bool Enabled,
    MisfirePolicy MisfirePolicy,
    DateTimeOffset NextRunAt,
    DateTimeOffset? LastEnqueueAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    string? PayloadTypeName,
    string TimeZoneId
);
