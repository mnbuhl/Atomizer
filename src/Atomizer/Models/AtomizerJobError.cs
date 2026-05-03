using Atomizer.Models.Base;

namespace Atomizer;

/// <summary>
/// Records a single failed attempt for an <see cref="AtomizerJob"/>.
/// </summary>
public class AtomizerJobError : Model
{
    /// <summary>
    /// Gets or sets the identifier of the job that failed.
    /// </summary>
    public Guid JobId { get; set; }

    /// <summary>
    /// Gets or sets the exception message from the failed attempt, or null if unavailable.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets the stack trace from the failed attempt, truncated to 5,120 characters.
    /// </summary>
    public string? StackTrace { get; set; }

    /// <summary>
    /// Gets or sets the fully qualified exception type name, or null if unavailable.
    /// </summary>
    public string? ExceptionType { get; set; }

    /// <summary>
    /// Gets or sets the UTC time this error record was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the attempt number on which this error occurred.
    /// </summary>
    public int Attempt { get; set; }

    /// <summary>
    /// Gets or sets the instance identifier of the worker that made this attempt.
    /// </summary>
    public string? RuntimeIdentity { get; set; }

    /// <summary>
    /// Creates a new <see cref="AtomizerJobError"/> capturing details from an exception.
    /// </summary>
    /// <param name="jobId">The identifier of the job that failed.</param>
    /// <param name="createdAt">The UTC time the error was recorded.</param>
    /// <param name="attempt">The attempt number on which the error occurred.</param>
    /// <param name="exception">The exception that caused the failure, or <see langword="null"/> if unavailable.</param>
    /// <param name="runtimeIdentity">The instance identifier of the worker that attempted the job.</param>
    /// <returns>A new <see cref="AtomizerJobError"/> instance.</returns>
    public static AtomizerJobError Create(
        Guid jobId,
        DateTimeOffset createdAt,
        int attempt,
        Exception? exception,
        string? runtimeIdentity
    )
    {
        var stackTrace = exception?.StackTrace;
        return new AtomizerJobError
        {
            Id = Guid.NewGuid(),
            JobId = jobId,
            ErrorMessage = exception?.Message,
            StackTrace = stackTrace?.Length > 5120 ? stackTrace.Substring(0, 5120) : stackTrace,
            ExceptionType = exception?.GetType().FullName,
            CreatedAt = createdAt,
            Attempt = attempt,
            RuntimeIdentity = runtimeIdentity,
        };
    }
}
