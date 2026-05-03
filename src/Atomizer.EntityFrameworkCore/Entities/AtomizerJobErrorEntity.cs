namespace Atomizer.EntityFrameworkCore.Entities;

/// <summary>
/// Database entity representing a single failed processing attempt for an Atomizer job.
/// </summary>
public class AtomizerJobErrorEntity
{
    /// <summary>Gets or sets the unique identifier of this error record.</summary>
    public Guid Id { get; set; }

    /// <summary>Gets or sets the identifier of the job this error belongs to.</summary>
    public Guid JobId { get; set; }

    /// <summary>Gets or sets the navigation property to the parent job entity.</summary>
    public AtomizerJobEntity? Job { get; set; }

    /// <summary>Gets or sets the exception message from the failed attempt.</summary>
    public string? ErrorMessage { get; set; }

    /// <summary>Gets or sets the stack trace from the failed attempt, truncated to 5120 characters.</summary>
    public string? StackTrace { get; set; }

    /// <summary>Gets or sets the fully qualified exception type name.</summary>
    public string? ExceptionType { get; set; }

    /// <summary>Gets or sets the UTC time at which the error was recorded.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets the attempt number on which this error occurred.</summary>
    public int Attempt { get; set; }

    /// <summary>Gets or sets the runtime identity of the worker that recorded this error.</summary>
    public string? RuntimeIdentity { get; set; }
}

/// <summary>
/// Provides mapping methods between <see cref="AtomizerJobError"/> domain objects and <see cref="AtomizerJobErrorEntity"/> records.
/// </summary>
public static class AtomizerJobErrorEntityMapper
{
    /// <summary>
    /// Maps a domain <see cref="AtomizerJobError"/> to its <see cref="AtomizerJobErrorEntity"/> database representation.
    /// </summary>
    /// <param name="error">The domain error to map.</param>
    /// <returns>A new <see cref="AtomizerJobErrorEntity"/> populated from the domain object.</returns>
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
            ExceptionType = error.ExceptionType,
        };
    }

    /// <summary>
    /// Maps an <see cref="AtomizerJobErrorEntity"/> database record to its domain <see cref="AtomizerJobError"/> representation.
    /// </summary>
    /// <param name="entity">The entity to map.</param>
    /// <returns>A new <see cref="AtomizerJobError"/> populated from the entity.</returns>
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
            ExceptionType = entity.ExceptionType,
        };
    }
}
