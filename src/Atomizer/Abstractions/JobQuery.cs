namespace Atomizer;

/// <summary>
/// Defines filter and pagination parameters for querying jobs.
/// </summary>
public sealed class JobQuery
{
    /// <summary>
    /// Filter by one or more job statuses. Null returns all statuses.
    /// </summary>
    public IReadOnlyCollection<AtomizerJobStatus>? Statuses { get; init; }

    /// <summary>
    /// Filter by queue key. Null returns jobs from all queues.
    /// </summary>
    public QueueKey? QueueKey { get; init; }

    /// <summary>
    /// Filter by payload type name substring (case-insensitive). Null returns all types.
    /// </summary>
    public string? PayloadTypeName { get; init; }

    /// <summary>
    /// Return only jobs created at or after this UTC timestamp.
    /// </summary>
    public DateTimeOffset? CreatedFromUtc { get; init; }

    /// <summary>
    /// Return only jobs created at or before this UTC timestamp.
    /// </summary>
    public DateTimeOffset? CreatedToUtc { get; init; }

    /// <summary>
    /// Number of records to skip for pagination. Defaults to 0.
    /// </summary>
    public int Skip { get; init; } = 0;

    /// <summary>
    /// Maximum number of records to return. Defaults to 50. Hard ceiling of 500.
    /// </summary>
    public int Take { get; init; } = 50;
}
