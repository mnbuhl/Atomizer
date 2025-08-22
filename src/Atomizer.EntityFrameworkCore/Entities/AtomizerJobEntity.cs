using System;
using System.Collections.Generic;
using System.Linq;

namespace Atomizer.EntityFrameworkCore.Entities;

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
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public DateTimeOffset? FailedAt { get; set; }
    public string? LeaseToken { get; set; }
    public string? ScheduleJobKey { get; set; }
    public string? IdempotencyKey { get; set; }
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
            MaxAttempts = job.MaxAttempts,
            ScheduleJobKey = job.ScheduleJobKey?.ToString(),
            IdempotencyKey = job.IdempotencyKey,
            Errors = job.Errors.Select(err => err.ToEntity()).ToList(),
        };
    }

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
            MaxAttempts = entity.MaxAttempts,
            ScheduleJobKey = entity.ScheduleJobKey != null ? new JobKey(entity.ScheduleJobKey) : null,
            IdempotencyKey = entity.IdempotencyKey,
            Errors = entity.Errors.Select(err => err.ToAtomizerJobError()).ToList(),
        };
    }
}
