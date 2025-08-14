using System;
using Atomizer.Models;

namespace Atomizer.EntityFrameworkCore.Entities
{
    public class AtomizerJobEntity
    {
        public Guid Id { get; set; }
        public string QueueKey { get; set; } = string.Empty;
        public string PayloadType { get; set; } = string.Empty;
        public string Payload { get; set; } = string.Empty;
        public DateTimeOffset ScheduledAt { get; set; }
        public DateTimeOffset? VisibleAt { get; set; }
        public AtomizerEntityJobStatus Status { get; set; } = AtomizerEntityJobStatus.Pending;
        public int Attempt { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
        public DateTimeOffset? FailedAt { get; set; }

        public string? CorrelationId { get; set; }
        public string? CausationId { get; set; }
        public string? IdempotencyKey { get; set; }

        public string? LeaseToken { get; set; }
    }

    public enum AtomizerEntityJobStatus
    {
        Pending,
        Processing,
        Completed,
        Failed,
        DeadLettered,
    }

    public static class AtomizerJobEntityExtensions
    {
        public static AtomizerJobEntity ToEntity(this AtomizerJob job)
        {
            return new AtomizerJobEntity
            {
                Id = job.Id,
                QueueKey = job.QueueKey.ToString(),
                PayloadType = job.PayloadType.AssemblyQualifiedName ?? string.Empty,
                Payload = job.Payload,
                ScheduledAt = job.ScheduledAt,
                VisibleAt = job.VisibleAt,
                Status = (AtomizerEntityJobStatus)(int)job.Status,
                Attempt = job.Attempt,
                CreatedAt = job.CreatedAt,
                CompletedAt = job.CompletedAt,
                FailedAt = job.FailedAt,
                CorrelationId = job.CorrelationId,
                CausationId = job.CausationId,
                IdempotencyKey = job.IdempotencyKey,
            };
        }
    }

    public static class AtomizerJobEntityMapper
    {
        public static AtomizerJob ToAtomizerJob(this AtomizerJobEntity entity)
        {
            return new AtomizerJob
            {
                Id = entity.Id,
                QueueKey = new QueueKey(entity.QueueKey),
                PayloadType =
                    Type.GetType(entity.PayloadType) ?? throw new InvalidOperationException("Invalid payload type"),
                Payload = entity.Payload,
                ScheduledAt = entity.ScheduledAt,
                VisibleAt = entity.VisibleAt,
                Status = (AtomizerJobStatus)(int)entity.Status,
                Attempt = entity.Attempt,
                CreatedAt = entity.CreatedAt,
                CompletedAt = entity.CompletedAt,
                FailedAt = entity.FailedAt,
                CorrelationId = entity.CorrelationId,
                CausationId = entity.CausationId,
                IdempotencyKey = entity.IdempotencyKey,
            };
        }
    }
}
