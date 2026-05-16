namespace Atomizer;

/// <summary>
/// Job counts broken down by lifecycle status.
/// </summary>
public sealed class JobStatusCounts
{
    /// <summary>Number of jobs with status Pending.</summary>
    public int Pending { get; init; }

    /// <summary>Number of jobs with status Processing.</summary>
    public int Processing { get; init; }

    /// <summary>Number of jobs with status Completed.</summary>
    public int Completed { get; init; }

    /// <summary>Number of jobs with status Failed.</summary>
    public int Failed { get; init; }

    /// <summary>Number of jobs with status Cancelled.</summary>
    public int Cancelled { get; init; }
}
