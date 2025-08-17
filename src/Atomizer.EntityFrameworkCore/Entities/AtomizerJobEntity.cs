using System;
using System.Collections.Generic;
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
        public int Attempts { get; set; }
        public int MaxAttempts { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset? CompletedAt { get; set; }
        public DateTimeOffset? FailedAt { get; set; }
        public string? IdempotencyKey { get; set; }
        public string? LeaseToken { get; set; }
        public List<AtomizerJobErrorEntity> Errors { get; set; } = new List<AtomizerJobErrorEntity>();
    }

    public enum AtomizerEntityJobStatus
    {
        Pending = 1,
        Processing = 2,
        Completed = 3,
        Failed = 4,
    }

    public static class AtomizerJobEntityMapper
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
                Attempts = job.Attempts,
                CreatedAt = job.CreatedAt,
                CompletedAt = job.CompletedAt,
                FailedAt = job.FailedAt,
                IdempotencyKey = job.IdempotencyKey,
                LeaseToken = job.LeaseToken?.Token,
                MaxAttempts = job.MaxAttempts,
            };
        }

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
                Attempts = entity.Attempts,
                CreatedAt = entity.CreatedAt,
                CompletedAt = entity.CompletedAt,
                FailedAt = entity.FailedAt,
                IdempotencyKey = entity.IdempotencyKey,
                LeaseToken = entity.LeaseToken != null ? new LeaseToken(entity.LeaseToken) : null,
                MaxAttempts = entity.MaxAttempts,
            };
        }
    }
}
