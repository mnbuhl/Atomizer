using System;

namespace Atomizer.Abstractions
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
        public int Attempt { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
        public DateTimeOffset? FailedAt { get; set; }
        public string? FifoKey { get; set; }

        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public string? IdempotencyKey { get; set; }

        public enum AtomizerJobStatus
        {
            Pending,
            Processing,
            Completed,
            Failed,
            DeadLettered,
        }
    }
}
