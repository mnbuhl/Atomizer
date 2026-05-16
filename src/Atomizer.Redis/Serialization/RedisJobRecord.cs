namespace Atomizer.Redis.Serialization;

internal sealed class RedisJobRecord
{
    public Guid Id { get; set; }

    public string QueueKey { get; set; } = string.Empty;

    public string? PayloadType { get; set; }

    public string Payload { get; set; } = string.Empty;

    public DateTimeOffset ScheduledAt { get; set; }

    public DateTimeOffset? VisibleAt { get; set; }

    public AtomizerJobStatus Status { get; set; }

    public int Attempts { get; set; }

    public long[] RetryIntervalTicks { get; set; } = Array.Empty<long>();

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public DateTimeOffset? FailedAt { get; set; }

    public string? LeaseToken { get; set; }

    public string? ScheduleJobKey { get; set; }

    public string? IdempotencyKey { get; set; }

    public string? PartitionKey { get; set; }

    public long? SequenceNumber { get; set; }

    public List<RedisJobErrorRecord> Errors { get; set; } = new List<RedisJobErrorRecord>();

    public static RedisJobRecord FromJob(AtomizerJob job) =>
        new RedisJobRecord
        {
            Id = job.Id,
            QueueKey = job.QueueKey.Key,
            PayloadType = job.PayloadType?.AssemblyQualifiedName,
            Payload = job.Payload,
            ScheduledAt = job.ScheduledAt,
            VisibleAt = job.VisibleAt,
            Status = job.Status,
            Attempts = job.Attempts,
            RetryIntervalTicks = job.RetryStrategy.RetryIntervals.Select(interval => interval.Ticks).ToArray(),
            CreatedAt = job.CreatedAt,
            UpdatedAt = job.UpdatedAt,
            CompletedAt = job.CompletedAt,
            FailedAt = job.FailedAt,
            LeaseToken = job.LeaseToken?.Token,
            ScheduleJobKey = job.ScheduleJobKey?.Key,
            IdempotencyKey = job.IdempotencyKey,
            PartitionKey = job.PartitionKey?.Key,
            SequenceNumber = job.SequenceNumber,
            Errors = job.Errors.Select(RedisJobErrorRecord.FromError).ToList(),
        };

    public AtomizerJob ToJob() =>
        new AtomizerJob
        {
            Id = Id,
            QueueKey = new QueueKey(QueueKey),
            PayloadType = PayloadType is null ? null : Type.GetType(PayloadType),
            Payload = Payload,
            ScheduledAt = ScheduledAt,
            VisibleAt = VisibleAt,
            Status = Status,
            Attempts = Attempts,
            RetryStrategy =
                RetryIntervalTicks.Length == 0
                    ? RetryStrategy.None
                    : RetryStrategy.Intervals(RetryIntervalTicks.Select(TimeSpan.FromTicks)),
            CreatedAt = CreatedAt,
            UpdatedAt = UpdatedAt,
            CompletedAt = CompletedAt,
            FailedAt = FailedAt,
            LeaseToken = LeaseToken is null ? null : new LeaseToken(LeaseToken),
            ScheduleJobKey = ScheduleJobKey is null ? null : new JobKey(ScheduleJobKey),
            IdempotencyKey = IdempotencyKey,
            PartitionKey = PartitionKey is null ? null : new PartitionKey(PartitionKey),
            SequenceNumber = SequenceNumber,
            Errors = Errors.Select(error => error.ToError()).ToList(),
        };
}
