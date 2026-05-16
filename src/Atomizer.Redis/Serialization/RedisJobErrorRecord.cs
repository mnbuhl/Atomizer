namespace Atomizer.Redis.Serialization;

internal sealed class RedisJobErrorRecord
{
    public Guid Id { get; set; }

    public Guid JobId { get; set; }

    public string? ErrorMessage { get; set; }

    public string? StackTrace { get; set; }

    public string? ExceptionType { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public int Attempt { get; set; }

    public string? RuntimeIdentity { get; set; }

    public static RedisJobErrorRecord FromError(AtomizerJobError error) =>
        new RedisJobErrorRecord
        {
            Id = error.Id,
            JobId = error.JobId,
            ErrorMessage = error.ErrorMessage,
            StackTrace = error.StackTrace,
            ExceptionType = error.ExceptionType,
            CreatedAt = error.CreatedAt,
            Attempt = error.Attempt,
            RuntimeIdentity = error.RuntimeIdentity,
        };

    public AtomizerJobError ToError() =>
        new AtomizerJobError
        {
            Id = Id,
            JobId = JobId,
            ErrorMessage = ErrorMessage,
            StackTrace = StackTrace,
            ExceptionType = ExceptionType,
            CreatedAt = CreatedAt,
            Attempt = Attempt,
            RuntimeIdentity = RuntimeIdentity,
        };
}
