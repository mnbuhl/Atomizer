namespace Atomizer;

/// <summary>
/// A point-in-time snapshot of aggregated Atomizer system statistics, covering
/// queue depths, job-status breakdowns, processing throughput indicators,
/// error counts, and recurring-schedule summaries.
/// Consumed by the Atomizer Dashboard and any custom monitoring code.
/// </summary>
/// <param name="Queues">
/// Per-queue statistics. One <see cref="QueueStats"/> entry per distinct queue that has
/// at least one job, ordered alphabetically by queue name.
/// </param>
/// <param name="TotalPending">
/// Total number of jobs across all queues currently in
/// <see cref="AtomizerJobStatus.Pending"/> status.
/// </param>
/// <param name="TotalProcessing">
/// Total number of jobs across all queues currently in
/// <see cref="AtomizerJobStatus.Processing"/> status.
/// </param>
/// <param name="TotalCompleted">
/// Total number of jobs across all queues that have reached
/// <see cref="AtomizerJobStatus.Completed"/> status.
/// A rising value indicates processing throughput.
/// </param>
/// <param name="TotalFailed">
/// Total number of jobs across all queues that have reached
/// <see cref="AtomizerJobStatus.Failed"/> status after exhausting all retry attempts.
/// </param>
/// <param name="TotalCancelled">
/// Total number of jobs across all queues that have been explicitly cancelled
/// via <see cref="AtomizerJobStatus.Cancelled"/> and will not be processed.
/// </param>
/// <param name="TotalErrors">
/// Cumulative count of individual error records stored across all jobs. Each
/// failed attempt by the processing pipeline records one error, so a job that
/// exhausts three retries contributes three to this total.
/// </param>
/// <param name="TotalSchedules">
/// Total number of recurring schedules registered in the storage backend,
/// regardless of enabled/disabled state.
/// </param>
/// <param name="EnabledSchedules">
/// Number of recurring schedules that are currently enabled and will be
/// triggered by the scheduler service.
/// </param>
/// <param name="GeneratedAt">
/// The UTC timestamp at which this snapshot was produced. Callers can use
/// two successive snapshots to derive processing-rate metrics (e.g. completed
/// jobs per second) by computing deltas of <see cref="TotalCompleted"/> over
/// the elapsed <see cref="GeneratedAt"/> interval.
/// </param>
public sealed record AtomizerStats(
    IReadOnlyList<QueueStats> Queues,
    int TotalPending,
    int TotalProcessing,
    int TotalCompleted,
    int TotalFailed,
    int TotalCancelled,
    int TotalErrors,
    int TotalSchedules,
    int EnabledSchedules,
    DateTimeOffset GeneratedAt
)
{
    /// <summary>
    /// Total number of jobs across all queues and all statuses.
    /// Equivalent to
    /// <see cref="TotalPending"/> + <see cref="TotalProcessing"/> +
    /// <see cref="TotalCompleted"/> + <see cref="TotalFailed"/> +
    /// <see cref="TotalCancelled"/>.
    /// </summary>
    public int TotalJobs => TotalPending + TotalProcessing + TotalCompleted + TotalFailed + TotalCancelled;

    /// <summary>
    /// Returns an empty <see cref="AtomizerStats"/> snapshot — all numeric fields are zero
    /// and <see cref="Queues"/> is empty — stamped with <paramref name="generatedAt"/>.
    /// Useful as a safe default when storage is unavailable.
    /// </summary>
    /// <param name="generatedAt">The UTC timestamp to record on the empty snapshot.</param>
    /// <returns>A zeroed-out <see cref="AtomizerStats"/> instance.</returns>
    public static AtomizerStats Empty(DateTimeOffset generatedAt) =>
        new(
            Queues: Array.Empty<QueueStats>(),
            TotalPending: 0,
            TotalProcessing: 0,
            TotalCompleted: 0,
            TotalFailed: 0,
            TotalCancelled: 0,
            TotalErrors: 0,
            TotalSchedules: 0,
            EnabledSchedules: 0,
            GeneratedAt: generatedAt
        );
}
