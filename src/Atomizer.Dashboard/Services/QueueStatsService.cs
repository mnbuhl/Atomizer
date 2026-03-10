using Atomizer;
using Atomizer.Abstractions;
using Microsoft.Extensions.Logging;

namespace Atomizer.Dashboard.Services;

/// <summary>
/// Data-access service that retrieves aggregated per-queue job statistics from the Atomizer
/// storage layer. Errors are caught and logged so that the dashboard UI can display a graceful
/// error state rather than crash the Blazor circuit.
/// </summary>
public sealed class QueueStatsService
{
    private readonly IAtomizerDashboardStorage _storage;
    private readonly ILogger<QueueStatsService> _logger;

    /// <summary>
    /// Initialises a new instance of <see cref="QueueStatsService"/>.
    /// </summary>
    /// <param name="storage">
    /// The dashboard-capable storage implementation used to query queue statistics.
    /// Both built-in backends (InMemory and EF Core) implement
    /// <see cref="IAtomizerDashboardStorage"/>.
    /// </param>
    /// <param name="logger">Logger used for diagnostic output on storage errors.</param>
    public QueueStatsService(IAtomizerDashboardStorage storage, ILogger<QueueStatsService> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    /// <summary>
    /// Returns the latest per-queue job counts from storage. Each entry contains pending,
    /// processing, completed, and failed counts for a single queue.
    /// </summary>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>
    /// A read-only list of <see cref="QueueStats"/> records, one per queue, ordered
    /// alphabetically by queue name. Returns an empty list when no jobs exist or when the
    /// storage call fails.
    /// </returns>
    public async Task<IReadOnlyList<QueueStats>> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _storage.GetQueueStatsAsync(cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to retrieve queue statistics from storage.");
            return Array.Empty<QueueStats>();
        }
    }
}
