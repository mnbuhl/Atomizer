using Atomizer.Core;
using Atomizer.Dashboard;
using Atomizer.EntityFrameworkCore.Entities;
using Microsoft.EntityFrameworkCore;

namespace Atomizer.EntityFrameworkCore.Dashboard;

internal sealed class EntityFrameworkCoreDashboardStorage<TContext> : IAtomizerDashboardStorage
    where TContext : DbContext
{
    private readonly IDbContextFactory<TContext> _dbContextFactory;
    private readonly IAtomizerClock _clock;

    public EntityFrameworkCoreDashboardStorage(IDbContextFactory<TContext> dbContextFactory, IAtomizerClock clock)
    {
        _dbContextFactory = dbContextFactory;
        _clock = clock;
    }

    public async Task<PagedResult<AtomizerJob>> GetJobsAsync(JobQuery query, CancellationToken cancellationToken)
    {
        var take = Math.Min(query.Take, 500);

        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        IQueryable<AtomizerJobEntity> q = db.Set<AtomizerJobEntity>().AsNoTracking();

        if (query.Statuses is { Count: > 0 })
        {
            var statuses = query.Statuses.Select(s => (AtomizerEntityJobStatus)(int)s).ToList();
            q = q.Where(e => statuses.Contains(e.Status));
        }

        if (query.QueueKey is not null)
            q = q.Where(e => e.QueueKey == query.QueueKey.ToString());

        if (query.PayloadTypeName is not null)
            q = q.Where(e => e.PayloadType.Contains(query.PayloadTypeName));

        if (query.CreatedFromUtc.HasValue)
            q = q.Where(e => e.CreatedAt >= query.CreatedFromUtc.Value);

        if (query.CreatedToUtc.HasValue)
            q = q.Where(e => e.CreatedAt <= query.CreatedToUtc.Value);

        q = q.OrderByDescending(e => e.CreatedAt);

        var total = await q.CountAsync(cancellationToken);

        var entities = await q.Skip(query.Skip).Take(take).Include(e => e.Errors).ToListAsync(cancellationToken);

        return new PagedResult<AtomizerJob>
        {
            Items = entities.Select(e => e.ToAtomizerJob()).ToList(),
            TotalCount = total,
            Skip = query.Skip,
            Take = take,
        };
    }

    public async Task<AtomizerJob?> GetJobByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var entity = await db.Set<AtomizerJobEntity>()
            .AsNoTracking()
            .Include(e => e.Errors)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

        return entity?.ToAtomizerJob();
    }

    public async Task<IReadOnlyList<AtomizerSchedule>> GetSchedulesAsync(CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var entities = await db.Set<AtomizerScheduleEntity>().AsNoTracking().ToListAsync(cancellationToken);

        return entities.Select(e => e.ToAtomizerSchedule()).ToList();
    }

    public async Task<IReadOnlyList<AtomizerActiveServer>> GetActiveServersAsync(CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var cutoff = _clock.UtcNow.AddMinutes(-5);

        var entities = await db.Set<AtomizerActiveServerEntity>()
            .AsNoTracking()
            .Where(e => e.LastHeartbeatAt >= cutoff)
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToAtomizerActiveServer()).ToList();
    }

    public async Task<IReadOnlyList<QueueStats>> GetQueueStatsAsync(CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);

        var stats = await db.Set<AtomizerJobEntity>()
            .AsNoTracking()
            .GroupBy(e => e.QueueKey)
            .Select(g => new
            {
                QueueKey = g.Key,
                Pending = g.Count(e => e.Status == AtomizerEntityJobStatus.Pending),
                Processing = g.Count(e => e.Status == AtomizerEntityJobStatus.Processing),
                Completed = g.Count(e => e.Status == AtomizerEntityJobStatus.Completed),
                Failed = g.Count(e => e.Status == AtomizerEntityJobStatus.Failed),
            })
            .ToListAsync(cancellationToken);

        return stats
            .Select(s => new QueueStats
            {
                QueueKey = new QueueKey(s.QueueKey),
                Pending = s.Pending,
                Processing = s.Processing,
                Completed = s.Completed,
                Failed = s.Failed,
            })
            .ToList();
    }
}
