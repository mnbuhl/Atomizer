using System;
using System.Collections.Generic;
using Atomizer.Models.Base;

namespace Atomizer
{
    public class AtomizerJob : Model
    {
        public QueueKey QueueKey { get; set; } = QueueKey.Default;
        public Type? PayloadType { get; set; }
        public string Payload { get; set; } = string.Empty;
        public DateTimeOffset ScheduledAt { get; set; }
        public DateTimeOffset? VisibleAt { get; set; }
        public AtomizerJobStatus Status { get; set; }
        public int Attempts { get; set; }
        public int MaxAttempts { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
        public DateTimeOffset? FailedAt { get; set; }
        public LeaseToken? LeaseToken { get; set; }
        public JobKey? ScheduleJobKey { get; set; }
        public string? IdempotencyKey { get; set; }
        public List<AtomizerJobError> Errors { get; set; } = new List<AtomizerJobError>();

        public static AtomizerJob Create(
            QueueKey queueKey,
            Type payloadType,
            string payload,
            DateTimeOffset createdAt,
            DateTimeOffset scheduledAt,
            int maxAttempts = 3,
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
                MaxAttempts = maxAttempts,
                CreatedAt = createdAt,
                UpdatedAt = createdAt,
                IdempotencyKey = idempotencyKey,
                ScheduleJobKey = scheduleJobKey,
            };
        }

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

        public void Attempt()
        {
            if (Status != AtomizerJobStatus.Processing)
            {
                throw new InvalidOperationException("Job must be in Processing status to attempt.");
            }

            Attempts += 1;
        }

        public void MarkAsCompleted(DateTimeOffset completedAt)
        {
            CompletedAt = completedAt;
            UpdatedAt = completedAt;
            Status = AtomizerJobStatus.Completed;
            LeaseToken = null;
            VisibleAt = null;
        }

        public void MarkAsFailed(DateTimeOffset failedAt)
        {
            FailedAt = failedAt;
            UpdatedAt = failedAt;
            Status = AtomizerJobStatus.Failed;
            LeaseToken = null;
            VisibleAt = null;
        }

        public void Retry(DateTimeOffset nextVisibleAt, DateTimeOffset now)
        {
            VisibleAt = nextVisibleAt;
            Status = AtomizerJobStatus.Pending;
            UpdatedAt = now;
            LeaseToken = null;
        }
    }

    public enum AtomizerJobStatus
    {
        Pending = 1,
        Processing = 2,
        Completed = 3,
        Failed = 4,
    }
}
