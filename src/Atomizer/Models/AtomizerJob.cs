using System;

namespace Atomizer.Models
{
    public class AtomizerJob
    {
        public Guid Id { get; set; }
        public QueueKey QueueKey { get; set; } = QueueKey.Default;
        public Type PayloadType { get; set; } = null!;
        public string Payload { get; set; } = string.Empty;
        public DateTimeOffset ScheduledAt { get; set; }
        public DateTimeOffset? VisibleAt { get; set; }
        public AtomizerJobStatus Status { get; set; } = AtomizerJobStatus.Pending;
        public int Attempts { get; set; }
        public int MaxAttempts { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
        public DateTimeOffset? FailedAt { get; set; }
        public string? IdempotencyKey { get; set; }
        public string? LeaseToken { get; set; }
    }

    public enum AtomizerJobStatus
    {
        Pending = 1,
        Processing = 2,
        Completed = 3,
        Failed = 4,
    }
}
