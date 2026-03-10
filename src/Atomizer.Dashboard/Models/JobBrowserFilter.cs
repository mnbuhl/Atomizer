namespace Atomizer.Dashboard.Models;

/// <summary>
/// Encapsulates the optional filter criteria used by the dashboard job browser.
/// All properties are optional; a <see langword="null"/> value disables that filter.
/// </summary>
public sealed class JobBrowserFilter
{
    /// <summary>
    /// When set, restricts results to jobs belonging to the queue with this name.
    /// Comparison is case-insensitive.
    /// </summary>
    public string? QueueName { get; init; }

    /// <summary>
    /// When set, restricts results to jobs that have the specified status.
    /// </summary>
    public AtomizerJobStatus? Status { get; init; }

    /// <summary>
    /// Returns <see langword="true"/> when no filter criteria are active,
    /// meaning all jobs should be returned.
    /// </summary>
    public bool IsEmpty => QueueName is null && Status is null;
}
