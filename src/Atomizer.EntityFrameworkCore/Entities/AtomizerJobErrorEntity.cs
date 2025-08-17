using System;
using Atomizer.Models;

namespace Atomizer.EntityFrameworkCore.Entities;

public class AtomizerJobErrorEntity
{
    public Guid Id { get; set; }
    public Guid JobId { get; set; }
    public AtomizerJobEntity? Job { get; set; }
    public string? ErrorMessage { get; set; }
    public string? StackTrace { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public int Attempt { get; set; }
    public string? RuntimeIdentity { get; set; }
}

public static class AtomizerJobErrorEntityMapper
{
    public static AtomizerJobErrorEntity ToEntity(this AtomizerJobError error)
    {
        return new AtomizerJobErrorEntity
        {
            Id = error.Id,
            JobId = error.JobId,
            ErrorMessage = error.ErrorMessage,
            StackTrace = error.StackTrace,
            CreatedAt = error.CreatedAt,
            Attempt = error.Attempt,
            RuntimeIdentity = error.RuntimeIdentity,
        };
    }

    public static AtomizerJobError ToAtomizerJobError(this AtomizerJobErrorEntity entity)
    {
        return new AtomizerJobError
        {
            Id = entity.Id,
            JobId = entity.JobId,
            ErrorMessage = entity.ErrorMessage,
            StackTrace = entity.StackTrace,
            CreatedAt = entity.CreatedAt,
            Attempt = entity.Attempt,
            RuntimeIdentity = entity.RuntimeIdentity,
        };
    }
}
