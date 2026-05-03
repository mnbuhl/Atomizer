using Atomizer.Models.Base;

namespace Atomizer;

/// <summary>
/// Represents a single job instance in the Atomizer processing pipeline.
/// </summary>
public class AtomizerJob : Model
{
    /// <summary>
    /// Gets or sets the key of the queue this job belongs to.
    /// </summary>
    public QueueKey QueueKey { get; set; } = QueueKey.Default;

    /// <summary>
    /// Gets or sets the CLR type of the job payload used for deserialization and handler resolution.
    /// </summary>
    public Type? PayloadType { get; set; }

    /// <summary>
    /// Gets or sets the serialized payload string.
    /// </summary>
    public string Payload { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the UTC time at which the job was originally scheduled to run.
    /// </summary>
    public DateTimeOffset ScheduledAt { get; set; }

    /// <summary>
    /// Gets or sets the UTC time after which the job becomes visible again for re-processing.
    /// </summary>
    public DateTimeOffset? VisibleAt { get; set; }

    /// <summary>
    /// Gets or sets the current status of the job.
    /// </summary>
    public AtomizerJobStatus Status { get; set; }

    /// <summary>
    /// Gets or sets the number of processing attempts made for this job.
    /// </summary>
    public int Attempts { get; set; }

    /// <summary>
    /// Gets or sets the retry strategy applied when the job fails.
    /// </summary>
    public RetryStrategy RetryStrategy { get; set; } = RetryStrategy.Default;

    /// <summary>
    /// Gets or sets the UTC time at which the job was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the UTC time at which the job was last updated.
    /// </summary>
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Gets or sets the UTC time at which the job completed successfully.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>
    /// Gets or sets the UTC time at which the job exhausted all retries and was marked failed.
    /// </summary>
    public DateTimeOffset? FailedAt { get; set; }

    /// <summary>
    /// Gets or sets the lease token held by the worker currently processing this job.
    /// </summary>
    public LeaseToken? LeaseToken { get; set; }

    /// <summary>
    /// Gets or sets the job key of the recurring schedule that generated this job, if any.
    /// </summary>
    public JobKey? ScheduleJobKey { get; set; }

    /// <summary>
    /// Gets or sets the idempotency key used to deduplicate job insertions.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    /// <summary>
    /// Gets or sets the list of error records captured from previous failed processing attempts.
    /// </summary>
    public List<AtomizerJobError> Errors { get; set; } = new List<AtomizerJobError>();

    /// <summary>
    /// Creates a new <see cref="AtomizerJob"/> in the <see cref="AtomizerJobStatus.Pending"/> state.
    /// </summary>
    /// <param name="queueKey">The queue the job should be placed in.</param>
    /// <param name="payloadType">The CLR type of the payload.</param>
    /// <param name="payload">The serialized payload string.</param>
    /// <param name="createdAt">The UTC time at which the job is being created.</param>
    /// <param name="scheduledAt">The UTC time at which the job should become eligible for processing.</param>
    /// <param name="retryStrategy">Optional retry strategy; defaults to <see cref="RetryStrategy.Default"/>.</param>
    /// <param name="idempotencyKey">Optional idempotency key to prevent duplicate insertions.</param>
    /// <param name="scheduleJobKey">Optional key of the recurring schedule that generated this job.</param>
    /// <returns>A new <see cref="AtomizerJob"/> instance.</returns>
    public static AtomizerJob Create(
        QueueKey queueKey,
        Type payloadType,
        string payload,
        DateTimeOffset createdAt,
        DateTimeOffset scheduledAt,
        RetryStrategy? retryStrategy = null,
        string? idempotencyKey = null,
        JobKey? scheduleJobKey = null
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
        };
    }

    /// <summary>
    /// Transitions the job to <see cref="AtomizerJobStatus.Processing"/> and records the lease.
    /// </summary>
    /// <param name="leaseToken">The lease token identifying the worker holding this job.</param>
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
    /// Returns the job to <see cref="AtomizerJobStatus.Pending"/> and clears the lease.
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
    /// Increments the attempt counter for this job.
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
    /// Marks the job as successfully completed.
    /// </summary>
    /// <param name="completedAt">The UTC time at which the job completed.</param>
    public void MarkAsCompleted(DateTimeOffset completedAt)
    {
        CompletedAt = completedAt;
        UpdatedAt = completedAt;
        Status = AtomizerJobStatus.Completed;
        LeaseToken = null;
        VisibleAt = null;
    }

    /// <summary>
    /// Marks the job as permanently failed after all retries are exhausted.
    /// </summary>
    /// <param name="failedAt">The UTC time at which the job was marked failed.</param>
    public void MarkAsFailed(DateTimeOffset failedAt)
    {
        FailedAt = failedAt;
        UpdatedAt = failedAt;
        Status = AtomizerJobStatus.Failed;
        LeaseToken = null;
        VisibleAt = null;
    }

    /// <summary>
    /// Returns the job to <see cref="AtomizerJobStatus.Pending"/> with a new visibility time for retry.
    /// </summary>
    /// <param name="nextVisibleAt">The UTC time after which the job becomes eligible for re-processing.</param>
    /// <param name="now">The current UTC time.</param>
    public void Reschedule(DateTimeOffset nextVisibleAt, DateTimeOffset now)
    {
        VisibleAt = nextVisibleAt;
        Status = AtomizerJobStatus.Pending;
        UpdatedAt = now;
        LeaseToken = null;
    }
}

/// <summary>
/// Represents the processing lifecycle status of an <see cref="AtomizerJob"/>.
/// </summary>
public enum AtomizerJobStatus
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
