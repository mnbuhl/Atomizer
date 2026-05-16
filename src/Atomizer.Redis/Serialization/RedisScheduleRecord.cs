namespace Atomizer.Redis.Serialization;

internal sealed class RedisScheduleRecord
{
    public Guid Id { get; set; }

    public string JobKey { get; set; } = string.Empty;

    public string QueueKey { get; set; } = string.Empty;

    public string? PayloadType { get; set; }

    public string Payload { get; set; } = string.Empty;

    public string Schedule { get; set; } = string.Empty;

    public string TimeZone { get; set; } = "UTC";

    public MisfirePolicy MisfirePolicy { get; set; }

    public int MaxCatchUp { get; set; }

    public bool Enabled { get; set; }

    public string? PartitionKey { get; set; }

    public long[] RetryIntervalTicks { get; set; } = Array.Empty<long>();

    public DateTimeOffset NextRunAt { get; set; }

    public DateTimeOffset? LastEnqueueAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public static RedisScheduleRecord FromSchedule(AtomizerSchedule schedule) =>
        new RedisScheduleRecord
        {
            Id = schedule.Id,
            JobKey = schedule.JobKey.Key,
            QueueKey = schedule.QueueKey.Key,
            PayloadType = schedule.PayloadType?.AssemblyQualifiedName,
            Payload = schedule.Payload,
            Schedule = schedule.Schedule.ToString(),
            TimeZone = schedule.TimeZone.Id,
            MisfirePolicy = schedule.MisfirePolicy,
            MaxCatchUp = schedule.MaxCatchUp,
            Enabled = schedule.Enabled,
            PartitionKey = schedule.PartitionKey?.Key,
            RetryIntervalTicks = schedule.RetryStrategy.RetryIntervals.Select(interval => interval.Ticks).ToArray(),
            NextRunAt = schedule.NextRunAt,
            LastEnqueueAt = schedule.LastEnqueueAt,
            CreatedAt = schedule.CreatedAt,
            UpdatedAt = schedule.UpdatedAt,
        };

    public AtomizerSchedule ToSchedule() =>
        new AtomizerSchedule
        {
            Id = Id,
            JobKey = new JobKey(JobKey),
            QueueKey = new QueueKey(QueueKey),
            PayloadType = PayloadType is null ? null : Type.GetType(PayloadType),
            Payload = Payload,
            Schedule = Atomizer.Schedule.Cron(Schedule),
            TimeZone = TimeZoneInfo.FindSystemTimeZoneById(TimeZone),
            MisfirePolicy = MisfirePolicy,
            MaxCatchUp = MaxCatchUp,
            Enabled = Enabled,
            PartitionKey = PartitionKey is null ? null : new PartitionKey(PartitionKey),
            RetryStrategy =
                RetryIntervalTicks.Length == 0
                    ? RetryStrategy.None
                    : RetryStrategy.Intervals(RetryIntervalTicks.Select(TimeSpan.FromTicks)),
            NextRunAt = NextRunAt,
            LastEnqueueAt = LastEnqueueAt,
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
        };
}
