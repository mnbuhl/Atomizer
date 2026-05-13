namespace Atomizer.Dashboard.Contracts;

internal sealed class JobErrorDto
{
    public int Attempt { get; init; }
    public string? ExceptionType { get; init; }
    public string? Message { get; init; }
    public string? StackTrace { get; init; }
    public DateTimeOffset OccurredAt { get; init; }
    public string? RuntimeIdentity { get; init; }

    internal static JobErrorDto From(AtomizerJobError error) =>
        new()
        {
            Attempt = error.Attempt,
            ExceptionType = error.ExceptionType,
            Message = error.ErrorMessage,
            StackTrace = error.StackTrace,
            OccurredAt = error.CreatedAt,
            RuntimeIdentity = error.RuntimeIdentity,
        };
}
