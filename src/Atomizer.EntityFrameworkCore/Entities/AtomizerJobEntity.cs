namespace Atomizer.EntityFrameworkCore.Entities;

/// <summary>
/// Database entity representing a single Atomizer job record.
/// </summary>
public class AtomizerJobEntity
{
    /// <summary>Gets or sets the unique identifier of the job.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the queue key string this job belongs to.</summary>
    public string QueueKey { get; set; } = string.Empty;

    /// <summary>Gets or sets the assembly-qualified name of the payload type.</summary>
    public string PayloadType { get; set; } = string.Empty;

    /// <summary>Gets or sets the serialized payload string.</summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>Gets or sets the UTC time at which the job is scheduled to run.</summary>
    public DateTimeOffset ScheduledAt { get; set; }

    /// <summary>Gets or sets the UTC time after which the job becomes visible for re-processing.</summary>
    public DateTimeOffset? VisibleAt { get; set; }

    /// <summary>Gets or sets the current processing status of the job.</summary>
    public AtomizerEntityJobStatus Status { get; set; } = AtomizerEntityJobStatus.Pending;

    /// <summary>Gets or sets the number of processing attempts made for this job.</summary>
    public int Attempts { get; set; }

    /// <summary>Gets or sets the serialized retry intervals for this job.</summary>
    public TimeSpan[] RetryIntervals { get; set; } = [];

    /// <summary>Gets or sets the UTC time at which the job was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets the UTC time at which the job was last updated.</summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Gets or sets the UTC time at which the job completed successfully.</summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>Gets or sets the UTC time at which the job was marked failed.</summary>
    public DateTimeOffset? FailedAt { get; set; }

    /// <summary>Gets or sets the raw lease token string held by the worker processing this job.</summary>
    public string? LeaseToken { get; set; }

    /// <summary>Gets or sets the job key of the recurring schedule that generated this job, if any.</summary>
    public string? ScheduleJobKey { get; set; }

    /// <summary>Gets or sets the idempotency key used to deduplicate job insertions.</summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>Gets or sets the partition key grouping this job for FIFO processing, or null if unpartitioned.</summary>
    public string? PartitionKey { get; set; }

    /// <summary>Gets or sets the monotonically increasing sequence number within (queue, partition key), or null if unpartitioned.</summary>
    public long? SequenceNumber { get; set; }

    /// <summary>Gets or sets the list of error records from previous failed attempts.</summary>
    public List<AtomizerJobErrorEntity> Errors { get; set; } = new List<AtomizerJobErrorEntity>();
}

/// <summary>
/// Represents the processing lifecycle status of an <see cref="AtomizerJobEntity"/>.
/// </summary>
public enum AtomizerEntityJobStatus
{
    /// <summary>The job is waiting to be picked up for processing.</summary>
    Pending = 1,

    /// <summary>The job is currently being processed by a worker.</summary>
    Processing = 2,

    /// <summary>The job completed successfully.</summary>
    Completed = 3,

    /// <summary>The job failed and all retry attempts have been exhausted.</summary>
    Failed = 4,
}

/// <summary>
/// Provides mapping methods between <see cref="AtomizerJob"/> domain objects and <see cref="AtomizerJobEntity"/> records.
/// </summary>
public static class AtomizerJobEntityMapper
{
    /// <summary>
    /// Maps a domain <see cref="AtomizerJob"/> to its <see cref="AtomizerJobEntity"/> database representation.
    /// </summary>
    /// <param name="job">The domain job to map.</param>
    /// <returns>A new <see cref="AtomizerJobEntity"/> populated from the domain object.</returns>
    public static AtomizerJobEntity ToEntity(this AtomizerJob job)
    {
        return new AtomizerJobEntity
        {
            Id = job.Id,
            QueueKey = job.QueueKey.ToString(),
            PayloadType = job.PayloadType?.AssemblyQualifiedName ?? string.Empty,
            Payload = job.Payload,
            ScheduledAt = job.ScheduledAt,
            VisibleAt = job.VisibleAt,
            Status = (AtomizerEntityJobStatus)(int)job.Status,
            Attempts = job.Attempts,
            CreatedAt = job.CreatedAt,
            UpdatedAt = job.UpdatedAt,
            CompletedAt = job.CompletedAt,
            FailedAt = job.FailedAt,
            LeaseToken = job.LeaseToken?.Token,
            RetryIntervals = job.RetryStrategy.RetryIntervals,
            ScheduleJobKey = job.ScheduleJobKey?.ToString(),
            IdempotencyKey = job.IdempotencyKey,
            PartitionKey = job.PartitionKey?.ToString(),
            SequenceNumber = job.SequenceNumber,
            Errors = job.Errors.Select(err => err.ToEntity()).ToList(),
        };
    }

    /// <summary>
    /// Maps an <see cref="AtomizerJobEntity"/> database record to its domain <see cref="AtomizerJob"/> representation.
    /// </summary>
    /// <param name="entity">The entity to map.</param>
    /// <returns>A new <see cref="AtomizerJob"/> populated from the entity.</returns>
    public static AtomizerJob ToAtomizerJob(this AtomizerJobEntity entity)
    {
        return new AtomizerJob
        {
            Id = entity.Id,
            QueueKey = new QueueKey(entity.QueueKey),
            PayloadType = Type.GetType(entity.PayloadType),
            Payload = entity.Payload,
            ScheduledAt = entity.ScheduledAt,
            VisibleAt = entity.VisibleAt,
            Status = (AtomizerJobStatus)(int)entity.Status,
            Attempts = entity.Attempts,
            CreatedAt = entity.CreatedAt,
            UpdatedAt = entity.UpdatedAt,
            CompletedAt = entity.CompletedAt,
            FailedAt = entity.FailedAt,
            LeaseToken = entity.LeaseToken != null ? new LeaseToken(entity.LeaseToken) : null,
            RetryStrategy =
                entity.RetryIntervals.Length == 0 ? RetryStrategy.None : RetryStrategy.Intervals(entity.RetryIntervals),
            ScheduleJobKey = entity.ScheduleJobKey != null ? new JobKey(entity.ScheduleJobKey) : null,
            IdempotencyKey = entity.IdempotencyKey,
            PartitionKey = entity.PartitionKey != null ? new PartitionKey(entity.PartitionKey) : null,
            SequenceNumber = entity.SequenceNumber,
            Errors = entity.Errors.Select(err => err.ToAtomizerJobError()).ToList(),
        };
    }
}
