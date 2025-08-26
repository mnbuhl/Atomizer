namespace Atomizer.EntityFrameworkCore.Entities;

/// <summary>
/// Entity representing a scheduled job in Atomizer.
/// </summary>
public class AtomizerScheduleEntity
{
    /// <summary>
    /// Unique identifier for the schedule.
    /// </summary>
    public Guid Id { get; set; }

    /// <summary>
    /// Key identifying the job.
    /// </summary>
    public string JobKey { get; set; } = string.Empty;

    /// <summary>
    /// Key identifying the queue.
    /// </summary>
    public string QueueKey { get; set; } = string.Empty;

    /// <summary>
    /// The type name of the payload.
    /// </summary>
    public string PayloadType { get; set; } = string.Empty;

    /// <summary>
    /// The serialized payload.
    /// </summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>
    /// Cron expression for scheduling.
    /// </summary>
    public string Schedule { get; set; } = "0 0 0 * *";

    /// <summary>
    /// Time zone identifier.
    /// </summary>
    public string TimeZone { get; set; } = "UTC";

    /// <summary>
    /// Misfire policy for missed runs.
    /// </summary>
    public MisfirePolicyEntity MisfirePolicy { get; set; }

    /// <summary>
    /// Maximum number of catch-up runs.
    /// </summary>
    public int MaxCatchUp { get; set; } = 5;

    /// <summary>
    /// Indicates if the schedule is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Maximum number of attempts for a job.
    /// </summary>
    public TimeSpan[] RetryIntervals { get; set; } = [];

    /// <summary>
    /// The next scheduled run time.
    /// </summary>
    public DateTimeOffset NextRunAt { get; set; }

    /// <summary>
    /// The last time the job was enqueued.
    /// </summary>
    public DateTimeOffset? LastEnqueueAt { get; set; }

    /// <summary>
    /// The time the schedule was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// The time the schedule was last updated.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Lease token for distributed locking (serialized).
    /// </summary>
    public string? LeaseToken { get; set; }

    /// <summary>
    /// When the schedule becomes visible.
    /// </summary>
    public DateTimeOffset? VisibleAt { get; set; }
}

public enum MisfirePolicyEntity
{
    Ignore = 1,
    ExecuteNow = 2,
    CatchUp = 3,
}

public static class AtomizerScheduleEntityMapper
{
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
            LeaseToken = schedule.LeaseToken?.Token,
            VisibleAt = schedule.VisibleAt,
        };
    }

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
            LeaseToken = string.IsNullOrEmpty(entity.LeaseToken) ? null : new LeaseToken(entity.LeaseToken!),
            VisibleAt = entity.VisibleAt,
        };
    }
}
