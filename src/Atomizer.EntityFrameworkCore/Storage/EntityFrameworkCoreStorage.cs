using Atomizer.Abstractions;
using Atomizer.EntityFrameworkCore.Entities;
using Atomizer.EntityFrameworkCore.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Atomizer.EntityFrameworkCore.Storage;

internal sealed class EntityFrameworkCoreStorage<TDbContext> : IAtomizerDashboardStorage
    where TDbContext : DbContext
{
    private readonly TDbContext _dbContext;
    private readonly EntityFrameworkCoreJobStorageOptions _options;
    private readonly ILogger<EntityFrameworkCoreStorage<TDbContext>> _logger;
    private readonly RelationalProviderCache _providerCache;

    private DbSet<AtomizerJobEntity> JobEntities => _dbContext.Set<AtomizerJobEntity>();
    private DbSet<AtomizerJobErrorEntity> JobErrorEntities => _dbContext.Set<AtomizerJobErrorEntity>();
    private DbSet<AtomizerScheduleEntity> ScheduleEntities => _dbContext.Set<AtomizerScheduleEntity>();

    public EntityFrameworkCoreStorage(
        TDbContext dbContext,
        EntityFrameworkCoreJobStorageOptions options,
        ILogger<EntityFrameworkCoreStorage<TDbContext>> logger
    )
    {
        _dbContext = dbContext;
        _options = options;
        _logger = logger;
        _providerCache = RelationalProviderCache.Create(dbContext);
    }

    public async Task<Guid> InsertAsync(AtomizerJob job, CancellationToken cancellationToken)
    {
        var entity = job.ToEntity();

        // @todo: make idempotency key unique with index
        var enforceIdempotency = job.IdempotencyKey != null;

        if (enforceIdempotency)
        {
            var existing = await JobEntities
                .AsNoTracking()
                .FirstOrDefaultAsync(j => j.IdempotencyKey == job.IdempotencyKey, cancellationToken);

            if (existing != null)
            {
                _logger.LogDebug(
                    "Job with idempotency key {IdempotencyKey} already exists with ID {JobId}",
                    job.IdempotencyKey,
                    existing.Id
                );
                return existing.Id;
            }
        }

        JobEntities.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    public async Task UpdateJobsAsync(IEnumerable<AtomizerJob> jobs, CancellationToken cancellationToken)
    {
        try
        {
            JobEntities.UpdateRange(jobs.Select(j => j.ToEntity()));
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to update jobs");
        }
    }

    public async Task<IReadOnlyList<AtomizerJob>> GetDueJobsAsync(
        QueueKey queueKey,
        DateTimeOffset now,
        int batchSize,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_providerCache is { IsSupportedProvider: true, RawSqlProvider: not null })
        {
            var sql = _providerCache.RawSqlProvider.GetDueJobsAsync(queueKey, now, batchSize);

            var entities = await JobEntities.FromSqlInterpolated(sql).AsNoTracking().ToListAsync(cancellationToken);

            return entities.Select(job => job.ToAtomizerJob()).ToList();
        }

        if (!_providerCache.IsSupportedProvider && _options.AllowUnsafeProviderFallback)
        {
            return await JobEntities
                .AsNoTracking()
                .Where(j =>
                    j.QueueKey == queueKey.Key
                    && (
                        j.Status == AtomizerEntityJobStatus.Pending
                            && (j.VisibleAt == null || j.VisibleAt <= now)
                            && j.ScheduledAt <= now
                        || (j.Status == AtomizerEntityJobStatus.Processing && j.VisibleAt <= now) // lease expired
                    )
                )
                .OrderBy(j => j.ScheduledAt)
                .Take(batchSize)
                .Select(job => job.ToAtomizerJob())
                .ToListAsync(cancellationToken);
        }

        throw new NotSupportedException(
            "The current database provider is not supported. "
                + "To bypass this check, set AllowUnsafeProviderFallback to true in EntityFrameworkCoreJobStorageOptions. "
                + "Note that this may lead to unexpected behavior."
        );
    }

    public async Task<int> ReleaseLeasedAsync(
        LeaseToken leaseToken,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        if (_providerCache is { IsSupportedProvider: true, RawSqlProvider: not null })
        {
            var sql = _providerCache.RawSqlProvider.ReleaseLeasedJobsAsync(leaseToken, now);
            var result = await _dbContext.Database.ExecuteSqlInterpolatedAsync(sql, cancellationToken);
            return result;
        }

        var entities = await JobEntities
            .Where(j => j.LeaseToken == leaseToken.Token && j.Status == AtomizerEntityJobStatus.Processing)
            .ToListAsync(cancellationToken);

        foreach (var entity in entities)
        {
            entity.Status = AtomizerEntityJobStatus.Pending;
            entity.VisibleAt = null;
            entity.LeaseToken = null;
            entity.UpdatedAt = now;
        }

        try
        {
            return await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to release leased jobs for lease token {LeaseToken}", leaseToken.Token);
            return 0;
        }
    }

    public async Task<Guid> UpsertScheduleAsync(AtomizerSchedule schedule, CancellationToken cancellationToken)
    {
        var entity = schedule.ToEntity();

        var existing = await ScheduleEntities
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.JobKey == entity.JobKey, cancellationToken);

        if (existing is not null)
        {
            entity.Id = existing.Id;
            ScheduleEntities.Update(entity);
        }
        else
        {
            ScheduleEntities.Add(entity);
        }

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // Might fail due to race conditions in a distributed setup
            // Look into optimistic concurrency control later
            _logger.LogError(
                ex,
                "Failed to upsert schedule {ScheduleKey} for job {JobKey}",
                schedule.JobKey,
                schedule.JobKey
            );
        }

        return entity.Id;
    }

    public async Task UpdateSchedulesAsync(IEnumerable<AtomizerSchedule> schedules, CancellationToken cancellationToken)
    {
        try
        {
            ScheduleEntities.UpdateRange(schedules.Select(s => s.ToEntity()));
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to update schedules");
        }
    }

    public async Task<IReadOnlyList<AtomizerSchedule>> GetDueSchedulesAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_providerCache is { IsSupportedProvider: true, RawSqlProvider: not null })
        {
            var sql = _providerCache.RawSqlProvider.GetDueSchedulesAsync(now);

            var entities = await ScheduleEntities
                .FromSqlInterpolated(sql)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            return entities.Select(s => s.ToAtomizerSchedule()).ToList();
        }

        if (!_providerCache.IsSupportedProvider && _options.AllowUnsafeProviderFallback)
        {
            return await ScheduleEntities
                .AsNoTracking()
                .Where(s => s.Enabled && s.NextRunAt <= now)
                .OrderBy(s => s.NextRunAt)
                .Select(s => s.ToAtomizerSchedule())
                .ToListAsync(cancellationToken);
        }

        throw new NotSupportedException(
            "The current database provider is not supported. "
                + "To bypass this check, set AllowUnsafeProviderFallback to true in EntityFrameworkCoreJobStorageOptions. "
                + "Note that this may lead to unexpected behavior."
        );
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<QueueStats>> GetQueueStatsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var groups = await JobEntities
            .AsNoTracking()
            .GroupBy(j => j.QueueKey)
            .Select(g => new
            {
                QueueKey = g.Key,
                Pending = g.Count(j => j.Status == AtomizerEntityJobStatus.Pending),
                Processing = g.Count(j => j.Status == AtomizerEntityJobStatus.Processing),
                Completed = g.Count(j => j.Status == AtomizerEntityJobStatus.Completed),
                Failed = g.Count(j => j.Status == AtomizerEntityJobStatus.Failed),
            })
            .OrderBy(g => g.QueueKey)
            .ToListAsync(cancellationToken);

        return groups
            .Select(g => new QueueStats(new QueueKey(g.QueueKey), g.Pending, g.Processing, g.Completed, g.Failed))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AtomizerJob>> GetRecentJobsAsync(
        int skip,
        int take,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await JobEntities
            .AsNoTracking()
            .OrderByDescending(j => j.CreatedAt)
            .Skip(skip)
            .Take(take)
            .Select(j => j.ToAtomizerJob())
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AtomizerSchedule>> GetAllSchedulesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return await ScheduleEntities
            .AsNoTracking()
            .OrderBy(s => s.JobKey)
            .Select(s => s.ToAtomizerSchedule())
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AtomizerJob>> GetJobsAsync(
        JobFilter filter,
        int skip,
        int take,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var query = JobEntities.AsNoTracking().AsQueryable();

        if (filter.QueueName is not null)
        {
            var queueName = filter.QueueName;
            query = query.Where(j => j.QueueKey.Contains(queueName));
        }

        if (filter.Status is not null)
        {
            var entityStatus = (AtomizerEntityJobStatus)(int)filter.Status;
            query = query.Where(j => j.Status == entityStatus);
        }

        return await query
            .OrderByDescending(j => j.CreatedAt)
            .Skip(skip)
            .Take(take)
            .Select(j => j.ToAtomizerJob())
            .ToListAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task<AtomizerJob?> GetJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var entity = await JobEntities
            .AsNoTracking()
            .Include(j => j.Errors)
            .FirstOrDefaultAsync(j => j.Id == jobId, cancellationToken);

        return entity?.ToAtomizerJob();
    }

    /// <inheritdoc />
    public Task<AtomizerJob?> GetJobByIdAsync(Guid jobId, CancellationToken cancellationToken) =>
        GetJobAsync(jobId, cancellationToken);

    /// <inheritdoc />
    public async Task<AtomizerJob?> GetLastJobForScheduleAsync(JobKey jobKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var entity = await JobEntities
            .AsNoTracking()
            .Where(j => j.ScheduleJobKey == jobKey.Key)
            .OrderByDescending(j => j.UpdatedAt)
            .FirstOrDefaultAsync(cancellationToken);

        return entity?.ToAtomizerJob();
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<ScheduleRecord>> GetSchedulesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var entities = await ScheduleEntities.AsNoTracking().OrderBy(s => s.JobKey).ToListAsync(cancellationToken);

        return entities
            .Select(e => new ScheduleRecord(
                Id: e.Id,
                JobKey: e.JobKey,
                QueueKey: e.QueueKey,
                CronExpression: e.Schedule,
                Enabled: e.Enabled,
                MisfirePolicy: (MisfirePolicy)(int)e.MisfirePolicy,
                NextRunAt: e.NextRunAt,
                LastEnqueueAt: e.LastEnqueueAt,
                CreatedAt: e.CreatedAt,
                UpdatedAt: e.UpdatedAt,
                PayloadTypeName: string.IsNullOrEmpty(e.PayloadType) ? null : e.PayloadType,
                TimeZoneId: e.TimeZone
            ))
            .ToList();
    }

    /// <inheritdoc />
    public async Task<AtomizerStats> GetStatsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // Per-queue stats (reuses existing projection)
        var queueGroups = await JobEntities
            .AsNoTracking()
            .GroupBy(j => j.QueueKey)
            .Select(g => new
            {
                QueueKey = g.Key,
                Pending = g.Count(j => j.Status == AtomizerEntityJobStatus.Pending),
                Processing = g.Count(j => j.Status == AtomizerEntityJobStatus.Processing),
                Completed = g.Count(j => j.Status == AtomizerEntityJobStatus.Completed),
                Failed = g.Count(j => j.Status == AtomizerEntityJobStatus.Failed),
            })
            .OrderBy(g => g.QueueKey)
            .ToListAsync(cancellationToken);

        var queues = queueGroups
            .Select(g => new QueueStats(new QueueKey(g.QueueKey), g.Pending, g.Processing, g.Completed, g.Failed))
            .ToList();

        // Aggregate status counts (includes Cancelled which is not tracked per-queue)
        var statusCounts = await JobEntities
            .AsNoTracking()
            .GroupBy(j => j.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        int Count(AtomizerEntityJobStatus status) => statusCounts.FirstOrDefault(s => s.Status == status)?.Count ?? 0;

        var totalErrors = await JobErrorEntities.AsNoTracking().CountAsync(cancellationToken);

        var totalSchedules = await ScheduleEntities.AsNoTracking().CountAsync(cancellationToken);
        var enabledSchedules = await ScheduleEntities.AsNoTracking().CountAsync(s => s.Enabled, cancellationToken);

        return new AtomizerStats(
            Queues: queues,
            TotalPending: Count(AtomizerEntityJobStatus.Pending),
            TotalProcessing: Count(AtomizerEntityJobStatus.Processing),
            TotalCompleted: Count(AtomizerEntityJobStatus.Completed),
            TotalFailed: Count(AtomizerEntityJobStatus.Failed),
            TotalCancelled: Count(AtomizerEntityJobStatus.Cancelled),
            TotalErrors: totalErrors,
            TotalSchedules: totalSchedules,
            EnabledSchedules: enabledSchedules,
            GeneratedAt: DateTimeOffset.UtcNow
        );
    }
}
