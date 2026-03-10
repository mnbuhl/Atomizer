namespace Atomizer;

/// <summary>
/// Aggregated statistics for a single Atomizer queue, broken down by job status.
/// Consumed by the Atomizer Dashboard and any custom monitoring code.
/// </summary>
/// <param name="QueueKey">The queue these counts belong to.</param>
/// <param name="Pending">Number of jobs currently in <see cref="AtomizerJobStatus.Pending"/> status.</param>
/// <param name="Processing">Number of jobs currently in <see cref="AtomizerJobStatus.Processing"/> status.</param>
/// <param name="Completed">Number of jobs that have reached <see cref="AtomizerJobStatus.Completed"/> status.</param>
/// <param name="Failed">Number of jobs that have reached <see cref="AtomizerJobStatus.Failed"/> status.</param>
public sealed record QueueStats(QueueKey QueueKey, int Pending, int Processing, int Completed, int Failed)
{
    /// <summary>Gets the total number of jobs across all statuses.</summary>
    public int Total => Pending + Processing + Completed + Failed;
}
