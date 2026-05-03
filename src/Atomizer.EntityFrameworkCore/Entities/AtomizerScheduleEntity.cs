namespace Atomizer.EntityFrameworkCore.Entities;

/// <summary>
/// Database entity representing a recurring job schedule.
/// </summary>
public class AtomizerScheduleEntity
{
    /// <summary>Gets or sets the unique identifier for the schedule.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the key identifying this recurring schedule.</summary>
    public string JobKey { get; set; } = string.Empty;

    /// <summary>Gets or sets the key identifying the target queue.</summary>
    public string QueueKey { get; set; } = string.Empty;

    /// <summary>Gets or sets the assembly-qualified name of the payload type.</summary>
    public string PayloadType { get; set; } = string.Empty;

    /// <summary>Gets or sets the serialized payload passed to generated jobs.</summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>Gets or sets the cron expression string defining when jobs are generated.</summary>
    public string Schedule { get; set; } = "0 0 0 * *";

    /// <summary>Gets or sets the time zone identifier for cron evaluation.</summary>
    public string TimeZone { get; set; } = "UTC";

    /// <summary>Gets or sets the misfire policy applied when the schedule fires late.</summary>
    public MisfirePolicyEntity MisfirePolicy { get; set; }

    /// <summary>Gets or sets the maximum number of missed occurrences to catch up on.</summary>
    public int MaxCatchUp { get; set; } = 5;

    /// <summary>Gets or sets whether this schedule is currently active.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets the serialized retry intervals for generated jobs.</summary>
    public TimeSpan[] RetryIntervals { get; set; } = [];

    /// <summary>Gets or sets the UTC time at which the next occurrence is due.</summary>
    public DateTimeOffset NextRunAt { get; set; }

    /// <summary>Gets or sets the UTC time at which the most recent job was enqueued.</summary>
    public DateTimeOffset? LastEnqueueAt { get; set; }

    /// <summary>Gets or sets the UTC time at which this schedule was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets the UTC time at which this schedule was last updated.</summary>
    public DateTimeOffset UpdatedAt { get; set; }
}

/// <summary>
/// Represents the misfire policy for an <see cref="AtomizerScheduleEntity"/> as stored in the database.
/// </summary>
public enum MisfirePolicyEntity
{
    /// <summary>Skip the missed run and advance to the next scheduled occurrence.</summary>
    Ignore = 1,

    /// <summary>Enqueue one job immediately, then advance to the next scheduled occurrence.</summary>
    ExecuteNow = 2,

    /// <summary>Enqueue all missed occurrences up to the configured maximum.</summary>
    CatchUp = 3,
}

/// <summary>
/// Provides mapping methods between <see cref="AtomizerSchedule"/> domain objects and <see cref="AtomizerScheduleEntity"/> records.
/// </summary>
public static class AtomizerScheduleEntityMapper
{
    /// <summary>
    /// Maps a domain <see cref="AtomizerSchedule"/> to its <see cref="AtomizerScheduleEntity"/> database representation.
    /// </summary>
    /// <param name="schedule">The domain schedule to map.</param>
    /// <returns>A new <see cref="AtomizerScheduleEntity"/> populated from the domain object.</returns>
    public static AtomizerScheduleEntity ToEntity(this AtomizerSchedule schedule)
    {
        return new AtomizerScheduleEntity
        {
            Id = schedule.Id,
            JobKey = schedule.JobKey.ToString(),
            QueueKey = schedule.QueueKey.ToString(),
            PayloadType = schedule.PayloadType?.AssemblyQualifiedName ?? string.Empty,
            Payload = schedule.Payload,
            Schedule = schedule.Schedule.ToString(),
            TimeZone = schedule.TimeZone.Id,
            MisfirePolicy = (MisfirePolicyEntity)(int)schedule.MisfirePolicy,
            MaxCatchUp = schedule.MaxCatchUp,
            Enabled = schedule.Enabled,
            RetryIntervals = schedule.RetryStrategy.RetryIntervals,
            NextRunAt = schedule.NextRunAt,
            LastEnqueueAt = schedule.LastEnqueueAt,
            CreatedAt = schedule.CreatedAt,
            UpdatedAt = schedule.UpdatedAt,
        };
    }

    /// <summary>
    /// Maps an <see cref="AtomizerScheduleEntity"/> database record to its domain <see cref="AtomizerSchedule"/> representation.
    /// </summary>
    /// <param name="entity">The entity to map.</param>
    /// <returns>A new <see cref="AtomizerSchedule"/> populated from the entity.</returns>
    public static AtomizerSchedule ToAtomizerSchedule(this AtomizerScheduleEntity entity)
    {
        return new AtomizerSchedule
        {
            Id = entity.Id,
            JobKey = new JobKey(entity.JobKey),
            QueueKey = new QueueKey(entity.QueueKey),
            PayloadType = Type.GetType(entity.PayloadType),
            Payload = entity.Payload,
            Schedule = Schedule.Cron(entity.Schedule),
            TimeZone = TimeZoneInfo.FindSystemTimeZoneById(entity.TimeZone),
            MisfirePolicy = (MisfirePolicy)(int)entity.MisfirePolicy,
            MaxCatchUp = entity.MaxCatchUp,
            Enabled = entity.Enabled,
            RetryStrategy =
                entity.RetryIntervals.Length == 0 ? RetryStrategy.None : RetryStrategy.Intervals(entity.RetryIntervals),
            NextRunAt = entity.NextRunAt,
            LastEnqueueAt = entity.LastEnqueueAt,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
        };
    }
}
