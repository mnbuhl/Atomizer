namespace Atomizer.Dashboard;

/// <summary>
/// Provides read-only query access to Atomizer job and schedule data for dashboard display.
/// Implement this interface to support the dashboard with a custom storage backend.
/// </summary>
public interface IAtomizerDashboardStorage
{
    /// <summary>
    /// Returns a paginated, filtered list of jobs ordered by creation time descending.
    /// </summary>
    Task<PagedResult<AtomizerJob>> GetJobsAsync(JobQuery query, CancellationToken cancellationToken);

    /// <summary>
    /// Returns all registered recurring schedules.
    /// </summary>
    Task<IReadOnlyList<AtomizerSchedule>> GetSchedulesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Returns all active servers (instances with a recent heartbeat).
    /// </summary>
    Task<IReadOnlyList<AtomizerActiveServer>> GetActiveServersAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Returns job counts grouped by queue and status.
    /// </summary>
    Task<IReadOnlyList<QueueStats>> GetQueueStatsAsync(CancellationToken cancellationToken);
}
