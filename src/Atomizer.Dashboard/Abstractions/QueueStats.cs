namespace Atomizer.Dashboard;

/// <summary>
/// Job counts for a single queue, broken down by status.
/// </summary>
public sealed class QueueStats
{
    /// <summary>The queue these counts apply to.</summary>
    public QueueKey QueueKey { get; init; } = null!;

    /// <summary>Number of jobs with status Pending.</summary>
    public int Pending { get; init; }

    /// <summary>Number of jobs with status Processing.</summary>
    public int Processing { get; init; }

    /// <summary>Number of jobs with status Completed.</summary>
    public int Completed { get; init; }

    /// <summary>Number of jobs with status Failed.</summary>
    public int Failed { get; init; }
}
