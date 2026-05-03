using Atomizer.Models.Base;

namespace Atomizer;

/// <summary>
/// Records details of a single failed processing attempt for an <see cref="AtomizerJob"/>.
/// </summary>
public class AtomizerJobError : Model
{
    /// <summary>
    /// Gets or sets the identifier of the job this error belongs to.
    /// </summary>
    public Guid JobId { get; set; }

    /// <summary>
    /// Gets or sets the exception message captured from the failed attempt.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets the stack trace captured from the failed attempt, truncated to 5120 characters.
    /// </summary>
    public string? StackTrace { get; set; }

    /// <summary>
    /// Gets or sets the fully qualified exception type name.
    /// </summary>
    public string? ExceptionType { get; set; }

    /// <summary>
    /// Gets or sets the UTC time at which the error was recorded.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the attempt number on which this error occurred.
    /// </summary>
    public int Attempt { get; set; }

    /// <summary>
    /// Gets or sets the runtime identity of the worker that recorded this error.
    /// </summary>
    public string? RuntimeIdentity { get; set; }

    /// <summary>
    /// Creates a new <see cref="AtomizerJobError"/> from a failed processing attempt.
    /// </summary>
    /// <param name="jobId">The identifier of the job that failed.</param>
    /// <param name="createdAt">The UTC time at which the error occurred.</param>
    /// <param name="attempt">The attempt number on which the failure occurred.</param>
    /// <param name="exception">The exception that caused the failure, or <see langword="null"/> if not available.</param>
    /// <param name="runtimeIdentity">The identity string of the worker that processed the job.</param>
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
