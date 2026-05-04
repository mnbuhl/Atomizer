using Atomizer.Models.Base;

namespace Atomizer;

/// <summary>
/// Represents a single enqueued or scheduled job in the Atomizer system.
/// </summary>
public class AtomizerJob : Model
{
    /// <summary>
    /// Gets or sets the queue this job belongs to.
    /// </summary>
    public QueueKey QueueKey { get; set; } = QueueKey.Default;

    /// <summary>
    /// Gets or sets the CLR type of the serialized payload.
    /// </summary>
    public Type? PayloadType { get; set; }

    /// <summary>
    /// Gets or sets the serialized payload string.
    /// </summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the earliest UTC time at which this job may be processed.
    /// </summary>
    public DateTimeOffset ScheduledAt { get; set; }

    /// <summary>
    /// Gets or sets the UTC time after which this job becomes visible to workers again.
    /// </summary>
    public DateTimeOffset? VisibleAt { get; set; }

    /// <summary>
    /// Gets or sets the current lifecycle state of this job.
    /// </summary>
    public AtomizerJobStatus Status { get; set; }

    /// <summary>
    /// Gets or sets the number of processing attempts made so far.
    /// </summary>
    public int Attempts { get; set; }

    /// <summary>
    /// Gets or sets the retry strategy applied when this job fails.
    /// </summary>
    public RetryStrategy RetryStrategy { get; set; } = RetryStrategy.Default;

    /// <summary>
    /// Gets or sets the UTC time this job was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the UTC time this job was last modified.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets the UTC time this job completed successfully, or null if not yet completed.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// Gets or sets the UTC time this job was permanently failed, or null if not yet failed.
    /// </summary>
    public DateTimeOffset? FailedAt { get; set; }

    /// <summary>
    /// Gets or sets the lease token held by the worker currently processing this job, or null if not leased.
    /// </summary>
    public LeaseToken? LeaseToken { get; set; }

    /// <summary>
    /// Gets or sets the key of the recurring schedule that produced this job, or null for ad-hoc jobs.
    /// </summary>
    public JobKey? ScheduleJobKey { get; set; }

    /// <summary>
    /// Gets or sets the deduplication key used to prevent duplicate job insertions.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>
    /// Gets or sets the partition key that groups this job for ordered (FIFO) processing,
    /// or <see langword="null"/> if the job participates in no partition.
    /// </summary>
    public PartitionKey? PartitionKey { get; set; }

    /// <summary>
    /// Gets or sets the monotonically increasing sequence number within the job's
    /// (queue, partition key) group, or <see langword="null"/> for unpartitioned jobs.
    /// </summary>
    /// <remarks>
    /// Assigned atomically by storage at insert time. A value of <see langword="null"/>
    /// indicates either an unpartitioned job or a job not yet inserted into storage.
    /// </remarks>
    public long? SequenceNumber { get; set; }

    /// <summary>
    /// Gets whether this job is currently holding its partition, preventing
    /// later jobs in the same partition from being picked up.
    /// </summary>
    /// <remarks>
    /// A job holds its partition when it is actively <see cref="AtomizerJobStatus.Processing"/>,
    /// or when it is <see cref="AtomizerJobStatus.Pending"/> with prior attempts (retrying).
    /// Jobs without a <see cref="PartitionKey"/> always return <see langword="false"/>.
    /// </remarks>
    public bool IsPartitionBlocked =>
        PartitionKey != null &&
        (Status == AtomizerJobStatus.Processing ||
         (Status == AtomizerJobStatus.Pending && Attempts > 0));

    /// <summary>
    /// Gets or sets the list of error records from previous failed attempts.
    /// </summary>
    public List<AtomizerJobError> Errors { get; set; } = new List<AtomizerJobError>();

    /// <summary>
    /// Creates a new <see cref="AtomizerJob"/> in the <see cref="AtomizerJobStatus.Pending"/> state.
    /// </summary>
    /// <param name="queueKey">The queue to place the job in.</param>
    /// <param name="payloadType">The CLR type of the serialized payload.</param>
    /// <param name="payload">The serialized payload string.</param>
    /// <param name="createdAt">The UTC time the job was created.</param>
    /// <param name="scheduledAt">The earliest UTC time the job may be processed.</param>
    /// <param name="retryStrategy">Optional retry strategy; defaults to <see cref="RetryStrategy.Default"/>.</param>
    /// <param name="idempotencyKey">Optional key used to deduplicate identical jobs.</param>
    /// <param name="scheduleJobKey">Optional key linking this job to a recurring schedule.</param>
    /// <param name="partitionKey">Optional partition key for ordered (FIFO) processing within the queue.</param>
    /// <returns>A new <see cref="AtomizerJob"/> instance.</returns>
    public static AtomizerJob Create(
        QueueKey queueKey,
        Type payloadType,
        string payload,
        DateTimeOffset createdAt,
        DateTimeOffset scheduledAt,
        RetryStrategy? retryStrategy = null,
        string? idempotencyKey = null,
        JobKey? scheduleJobKey = null,
        PartitionKey? partitionKey = null
    )
    {
        return new AtomizerJob
        {
            Id = Guid.NewGuid(),
            QueueKey = queueKey,
            PayloadType = payloadType,
            Payload = payload,
            ScheduledAt = scheduledAt,
            Status = AtomizerJobStatus.Pending,
            Attempts = 0,
            RetryStrategy = retryStrategy ?? RetryStrategy.Default,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
            IdempotencyKey = idempotencyKey,
            ScheduleJobKey = scheduleJobKey,
            PartitionKey = partitionKey,
            SequenceNumber = null,
        };
    }

    /// <summary>
    /// Transitions the job from <see cref="AtomizerJobStatus.Pending"/> to <see cref="AtomizerJobStatus.Processing"/>.
    /// </summary>
    /// <param name="leaseToken">The lease token identifying the owning worker.</param>
    /// <param name="now">The current UTC time.</param>
    /// <param name="visibilityTimeout">How long the job remains invisible to other workers.</param>
    public void Lease(LeaseToken leaseToken, DateTimeOffset now, TimeSpan visibilityTimeout)
    {
        if (Status != AtomizerJobStatus.Pending)
        {
            throw new InvalidOperationException("Job must be in Pending status to lease.");
        }

        LeaseToken = leaseToken;
        VisibleAt = now.Add(visibilityTimeout);
        Status = AtomizerJobStatus.Processing;
        UpdatedAt = now;
    }

    /// <summary>
    /// Returns the job to <see cref="AtomizerJobStatus.Pending"/> and clears the lease, making it available for reprocessing.
    /// </summary>
    /// <param name="now">The current UTC time.</param>
    public void Release(DateTimeOffset now)
    {
        if (Status != AtomizerJobStatus.Processing)
        {
            throw new InvalidOperationException("Job must be in Processing status to release.");
        }

        LeaseToken = null;
        VisibleAt = null;
        Status = AtomizerJobStatus.Pending;
        UpdatedAt = now;
    }

    /// <summary>
    /// Increments the attempt counter. Must be called while the job is in <see cref="AtomizerJobStatus.Processing"/>.
    /// </summary>
    public void Attempt()
    {
        if (Status != AtomizerJobStatus.Processing)
        {
            throw new InvalidOperationException("Job must be in Processing status to attempt.");
        }

        Attempts += 1;
    }

    /// <summary>
    /// Transitions the job to <see cref="AtomizerJobStatus.Completed"/> and records the completion time.
    /// </summary>
    /// <param name="completedAt">The UTC time the job completed.</param>
    public void MarkAsCompleted(DateTimeOffset completedAt)
    {
        CompletedAt = completedAt;
        UpdatedAt = completedAt;
        Status = AtomizerJobStatus.Completed;
        LeaseToken = null;
        VisibleAt = null;
    }

    /// <summary>
    /// Transitions the job to <see cref="AtomizerJobStatus.Failed"/> and records the failure time.
    /// </summary>
    /// <param name="failedAt">The UTC time the job was permanently failed.</param>
    public void MarkAsFailed(DateTimeOffset failedAt)
    {
        FailedAt = failedAt;
        UpdatedAt = failedAt;
        Status = AtomizerJobStatus.Failed;
        LeaseToken = null;
        VisibleAt = null;
    }

    /// <summary>
    /// Returns the job to <see cref="AtomizerJobStatus.Pending"/> with a future visibility time for retry.
    /// </summary>
    /// <param name="nextVisibleAt">The UTC time at which the job becomes visible again.</param>
    /// <param name="now">The current UTC time used to set <see cref="AtomizerJob.UpdatedAt"/>.</param>
    public void Reschedule(DateTimeOffset nextVisibleAt, DateTimeOffset now)
    {
        VisibleAt = nextVisibleAt;
        Status = AtomizerJobStatus.Pending;
        UpdatedAt = now;
        LeaseToken = null;
    }
}

/// <summary>
/// Represents the lifecycle state of an <see cref="AtomizerJob"/>.
/// </summary>
public enum AtomizerJobStatus
{
    /// <summary>
    /// The job is waiting to be picked up for processing.
    /// </summary>
    Pending = 1,

    /// <summary>
    /// The job has been leased and is actively being processed.
    /// </summary>
    Processing = 2,

    /// <summary>
    /// The job handler completed successfully.
    /// </summary>
    Completed = 3,

    /// <summary>
    /// The job exhausted all retry attempts and will not be retried.
    /// </summary>
    Failed = 4,
}
