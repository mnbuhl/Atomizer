using Atomizer.Core;
using Atomizer.Storage;

namespace Atomizer.Dashboard.Storage;

internal sealed class InMemoryDashboardStorage : IAtomizerDashboardStorage
{
    private readonly InMemoryStorage _storage;
    private readonly IAtomizerClock _clock;

    public InMemoryDashboardStorage(InMemoryStorage storage, IAtomizerClock clock)
    {
        _storage = storage;
        _clock = clock;
    }

    public Task<PagedResult<AtomizerJob>> GetJobsAsync(JobQuery query, CancellationToken cancellationToken)
    {
        var take = Math.Min(query.Take, 500);

        IEnumerable<AtomizerJob> jobs = _storage.GetAllJobs();

        if (query.Statuses is { Count: > 0 })
            jobs = jobs.Where(j => query.Statuses.Contains(j.Status));

        if (query.QueueKey is not null)
            jobs = jobs.Where(j => j.QueueKey == query.QueueKey);

        if (query.PayloadTypeName is not null)
            jobs = jobs.Where(j =>
                j.PayloadType != null
                && j.PayloadType.Name.Contains(query.PayloadTypeName, StringComparison.OrdinalIgnoreCase)
            );

        if (query.CreatedFromUtc.HasValue)
            jobs = jobs.Where(j => j.CreatedAt >= query.CreatedFromUtc.Value);

        if (query.CreatedToUtc.HasValue)
            jobs = jobs.Where(j => j.CreatedAt <= query.CreatedToUtc.Value);

        var ordered = jobs.OrderByDescending(j => j.CreatedAt).ToList();
        var total = ordered.Count;
        var items = ordered.Skip(query.Skip).Take(take).ToList();

        return Task.FromResult(
            new PagedResult<AtomizerJob>
            {
                Items = items,
                TotalCount = total,
                Skip = query.Skip,
                Take = take,
            }
        );
    }

    public Task<IReadOnlyList<AtomizerSchedule>> GetSchedulesAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(_storage.GetAllSchedules());
    }

    public Task<IReadOnlyList<AtomizerActiveServer>> GetActiveServersAsync(CancellationToken cancellationToken)
    {
        return Task.FromResult(_storage.GetAllServers());
    }

    public Task<IReadOnlyList<QueueStats>> GetQueueStatsAsync(CancellationToken cancellationToken)
    {
        var groups = _storage
            .GetAllJobs()
            .GroupBy(j => j.QueueKey)
            .Select(g => new QueueStats
            {
                QueueKey = g.Key,
                Pending = g.Count(j => j.Status == AtomizerJobStatus.Pending),
                Processing = g.Count(j => j.Status == AtomizerJobStatus.Processing),
                Completed = g.Count(j => j.Status == AtomizerJobStatus.Completed),
                Failed = g.Count(j => j.Status == AtomizerJobStatus.Failed),
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<QueueStats>>(groups);
    }
}
