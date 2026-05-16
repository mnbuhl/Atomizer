using Atomizer.Abstractions;
using Atomizer.Core;
using Atomizer.EntityFrameworkCore.Entities;
using Atomizer.EntityFrameworkCore.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Atomizer.EntityFrameworkCore.Storage;

internal sealed class EntityFrameworkCoreStorage<TDbContext> : IAtomizerStorage
    where TDbContext : DbContext
{
    private readonly TDbContext _dbContext;
    private readonly EntityFrameworkCoreJobStorageOptions _options;
    private readonly ILogger<EntityFrameworkCoreStorage<TDbContext>> _logger;
    private readonly RelationalProviderCache _providerCache;
    private readonly IAtomizerClock _clock;

    private DbSet<AtomizerJobEntity> JobEntities => _dbContext.Set<AtomizerJobEntity>();
    private DbSet<AtomizerJobErrorEntity> JobErrorEntities => _dbContext.Set<AtomizerJobErrorEntity>();
    private DbSet<AtomizerScheduleEntity> ScheduleEntities => _dbContext.Set<AtomizerScheduleEntity>();
    private DbSet<AtomizerActiveServerEntity> ActiveServerEntities => _dbContext.Set<AtomizerActiveServerEntity>();

    public EntityFrameworkCoreStorage(
        TDbContext dbContext,
        EntityFrameworkCoreJobStorageOptions options,
        ILogger<EntityFrameworkCoreStorage<TDbContext>> logger,
        IAtomizerClock clock
    )
    {
        _dbContext = dbContext;
        _options = options;
        _logger = logger;
        _clock = clock;
        _providerCache = RelationalProviderCache.Create(dbContext);
        if (!_providerCache.IsSupportedProvider)
        {
            throw UnsupportedProviderException(_providerCache.ProviderName);
        }
    }

    public async Task UpsertHeartbeatAsync(AtomizerActiveServer server, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var existing = await ActiveServerEntities.FindAsync(new object[] { server.InstanceId }, cancellationToken);
        if (existing is null)
        {
            ActiveServerEntities.Add(server.ToEntity());
        }
        else
        {
            existing.LastHeartbeatAt = server.LastHeartbeatAt;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AtomizerActiveServer>> GetStaleServersAsync(
        DateTimeOffset staleBefore,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();
        return await ActiveServerEntities
            .AsNoTracking()
            .Where(server => server.LastHeartbeatAt < staleBefore)
            .OrderBy(server => server.LastHeartbeatAt)
            .Select(server => new AtomizerActiveServer
            {
                InstanceId = server.InstanceId,
                LastHeartbeatAt = server.LastHeartbeatAt,
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<AtomizerHeartbeatRecoveryResult> TryRecoverStaleServerAsync(
        string instanceId,
        DateTimeOffset staleBefore,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        var server = await ActiveServerEntities
            .Where(activeServer => activeServer.InstanceId == instanceId && activeServer.LastHeartbeatAt < staleBefore)
            .FirstOrDefaultAsync(cancellationToken);

        if (server is null)
        {
            await transaction.RollbackAsync(cancellationToken);
            return AtomizerHeartbeatRecoveryResult.NotRecovered(instanceId);
        }

        ActiveServerEntities.Remove(server);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(cancellationToken);
            return AtomizerHeartbeatRecoveryResult.NotRecovered(instanceId);
        }

        var tokenPrefix = instanceId + LeaseToken.Delimiter;
        var jobs = await JobEntities
            .Where(job =>
                job.Status == AtomizerEntityJobStatus.Processing
                && job.LeaseToken != null
                && job.LeaseToken.StartsWith(tokenPrefix)
            )
            .ToListAsync(cancellationToken);

        foreach (var job in jobs)
        {
            job.Status = AtomizerEntityJobStatus.Pending;
            job.LeaseToken = null;
            job.VisibleAt = null;
            job.UpdatedAt = now;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return new AtomizerHeartbeatRecoveryResult(instanceId, true, jobs.Count);
    }

    public async Task RemoveHeartbeatAsync(string instanceId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var server = await ActiveServerEntities.FindAsync(new object[] { instanceId }, cancellationToken);
        if (server is null)
        {
            return;
        }

        ActiveServerEntities.Remove(server);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<Guid> InsertAsync(AtomizerJob job, CancellationToken cancellationToken)
    {
        var entity = job.ToEntity();

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
                job.SequenceNumber = existing.SequenceNumber;
                return existing.Id;
            }
        }

        if (job.PartitionKey != null && _providerCache.Dialect is not null)
        {
            var sql = _providerCache.Dialect.InsertJobWithSequence(job);
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(sql, cancellationToken);

            var assigned = await JobEntities
                .Where(j => j.Id == job.Id)
                .Select(j => j.SequenceNumber)
                .FirstAsync(cancellationToken);
            job.SequenceNumber = assigned;
            return job.Id;
        }

        JobEntities.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    public async Task UpdateJobsAsync(IEnumerable<AtomizerJob> jobs, CancellationToken cancellationToken)
    {
        try
        {
            // Clear the change tracker before attaching updated entities to avoid
            // InvalidOperationException when the same entities were previously
            // tracked by InsertAsync (or a prior UpdateJobsAsync call) on this context.
            _dbContext.ChangeTracker.Clear();

            var newErrors = new List<AtomizerJobErrorEntity>();
            var jobEntities = jobs.Select(j =>
                {
                    var entity = j.ToEntity();
                    // Errors loaded via GetDueJobsAsync are always empty (no .Include); any errors
                    // present here are new records added by HandleFailureAsync that have never been
                    // persisted. UpdateRange would mark them Modified (not Added), producing a
                    // zero-row UPDATE and a DbUpdateConcurrencyException. Extract them first so
                    // they can be inserted via AddRange instead.
                    newErrors.AddRange(entity.Errors);
                    entity.Errors = [];
                    return entity;
                })
                .ToList();

            JobEntities.UpdateRange(jobEntities);

            if (newErrors.Count > 0)
                JobErrorEntities.AddRange(newErrors);

            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to update jobs");
            throw;
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

        if (_providerCache.Dialect is not null)
        {
            var sql = _providerCache.Dialect.GetDueJobs(queueKey, now, batchSize);

            var entities = await JobEntities.FromSqlInterpolated(sql).AsNoTracking().ToListAsync(cancellationToken);

            return entities.Select(job => job.ToAtomizerJob()).ToList();
        }

        throw UnsupportedProviderException(_providerCache.ProviderName);
    }

    public async Task<int> ReleaseLeasedAsync(
        LeaseToken leaseToken,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        if (_providerCache.Dialect is not null)
        {
            var sql = _providerCache.Dialect.ReleaseLeasedJobs(leaseToken, now);
            var result = await _dbContext.Database.ExecuteSqlInterpolatedAsync(sql, cancellationToken);
            return result;
        }

        throw UnsupportedProviderException(_providerCache.ProviderName);
    }

    public async Task<Guid> UpsertScheduleAsync(AtomizerSchedule schedule, CancellationToken cancellationToken)
    {
        var entity = schedule.ToEntity();

        if (_providerCache.Dialect is not null)
        {
            var now = _clock.UtcNow;
            var sql = _providerCache.Dialect.UpsertSchedule(schedule, now);
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(sql, cancellationToken);
            return await ScheduleEntities
                .Where(s => s.JobKey == entity.JobKey)
                .Select(s => s.Id)
                .FirstAsync(cancellationToken);
        }

        throw UnsupportedProviderException(_providerCache.ProviderName);
    }

    public async Task UpdateSchedulesAsync(IEnumerable<AtomizerSchedule> schedules, CancellationToken cancellationToken)
    {
        try
        {
            // Clear the change tracker before attaching updated entities to avoid
            // InvalidOperationException when the same entities were previously
            // tracked by UpsertScheduleAsync (or a prior UpdateSchedulesAsync call) on this context.
            _dbContext.ChangeTracker.Clear();
            ScheduleEntities.UpdateRange(schedules.Select(s => s.ToEntity()));
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to update schedules");
            throw;
        }
    }

    public async Task<IReadOnlyList<AtomizerSchedule>> GetDueSchedulesAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_providerCache.Dialect is not null)
        {
            var sql = _providerCache.Dialect.GetDueSchedules(now);

            var entities = await ScheduleEntities
                .FromSqlInterpolated(sql)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            return entities.Select(s => s.ToAtomizerSchedule()).ToList();
        }

        throw UnsupportedProviderException(_providerCache.ProviderName);
    }

    public async Task<PagedResult<AtomizerJob>> GetJobsAsync(JobQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var take = Math.Min(query.Take, 500);

        var q = ApplyJobQueryFilters(JobEntities.AsNoTracking(), query, includeStatusFilter: true);

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

    public async Task<JobStatusCounts> GetJobStatusCountsAsync(JobQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var q = ApplyJobQueryFilters(JobEntities.AsNoTracking(), query, includeStatusFilter: false);
        var counts = await q.GroupBy(e => e.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        return new JobStatusCounts
        {
            Pending = counts.FirstOrDefault(c => c.Status == AtomizerEntityJobStatus.Pending)?.Count ?? 0,
            Processing = counts.FirstOrDefault(c => c.Status == AtomizerEntityJobStatus.Processing)?.Count ?? 0,
            Completed = counts.FirstOrDefault(c => c.Status == AtomizerEntityJobStatus.Completed)?.Count ?? 0,
            Failed = counts.FirstOrDefault(c => c.Status == AtomizerEntityJobStatus.Failed)?.Count ?? 0,
            Cancelled = counts.FirstOrDefault(c => c.Status == AtomizerEntityJobStatus.Cancelled)?.Count ?? 0,
        };
    }

    private static IQueryable<AtomizerJobEntity> ApplyJobQueryFilters(
        IQueryable<AtomizerJobEntity> q,
        JobQuery query,
        bool includeStatusFilter
    )
    {
        if (includeStatusFilter && query.Statuses is { Count: > 0 })
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

        return q;
    }

    public async Task<AtomizerJob?> GetJobByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var entity = await JobEntities
            .AsNoTracking()
            .Include(e => e.Errors)
            .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

        return entity?.ToAtomizerJob();
    }

    public async Task<int> DeleteExpiredJobsAsync(DateTimeOffset terminalBefore, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var expiredJobIds = await JobEntities
            .AsNoTracking()
            .Where(job =>
                (job.Status == AtomizerEntityJobStatus.Completed && (job.CompletedAt ?? job.UpdatedAt) < terminalBefore)
                || (job.Status == AtomizerEntityJobStatus.Failed && (job.FailedAt ?? job.UpdatedAt) < terminalBefore)
                || (job.Status == AtomizerEntityJobStatus.Cancelled && job.UpdatedAt < terminalBefore)
            )
            .Select(job => job.Id)
            .ToListAsync(cancellationToken);

        if (expiredJobIds.Count == 0)
        {
            return 0;
        }

        _dbContext.ChangeTracker.Clear();
        JobEntities.RemoveRange(expiredJobIds.Select(id => new AtomizerJobEntity { Id = id }));

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
            return expiredJobIds.Count;
        }
        catch (DbUpdateException ex)
        {
            _logger.LogError(ex, "Failed to delete expired jobs");
            throw;
        }
    }

    public async Task<IReadOnlyList<AtomizerSchedule>> GetSchedulesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var entities = await ScheduleEntities.AsNoTracking().ToListAsync(cancellationToken);

        return entities.Select(e => e.ToAtomizerSchedule()).ToList();
    }

    public async Task<IReadOnlyList<AtomizerActiveServer>> GetActiveServersAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var cutoff = _clock.UtcNow.AddMinutes(-5);

        var entities = await ActiveServerEntities
            .AsNoTracking()
            .Where(e => e.LastHeartbeatAt >= cutoff)
            .ToListAsync(cancellationToken);

        return entities.Select(e => e.ToAtomizerActiveServer()).ToList();
    }

    public async Task<IReadOnlyList<QueueStats>> GetQueueStatsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var stats = await JobEntities
            .AsNoTracking()
            .GroupBy(e => e.QueueKey)
            .Select(g => new
            {
                QueueKey = g.Key,
                Pending = g.Count(e => e.Status == AtomizerEntityJobStatus.Pending),
                Processing = g.Count(e => e.Status == AtomizerEntityJobStatus.Processing),
                Completed = g.Count(e => e.Status == AtomizerEntityJobStatus.Completed),
                Failed = g.Count(e => e.Status == AtomizerEntityJobStatus.Failed),
                Cancelled = g.Count(e => e.Status == AtomizerEntityJobStatus.Cancelled),
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
                Cancelled = s.Cancelled,
            })
            .ToList();
    }

    public async Task<TResult> ExecuteInLeaseAsync<TResult>(
        QueueKey queue,
        Func<CancellationToken, Task<TResult>> callback,
        CancellationToken cancellationToken
    )
    {
        await using var scope = await DatabaseTransactionLeasingScope.StartTransaction(
            _dbContext,
            _options.LockTimeout,
            cancellationToken
        );

        if (!scope.Acquired)
            return default!;

        TResult result;
        try
        {
            result = await callback(cancellationToken);
        }
        catch
        {
            scope.Abort();
            throw;
        }

        return result;
    }

    public async Task ExecuteInLeaseAsync(
        QueueKey queue,
        Func<CancellationToken, Task> callback,
        CancellationToken cancellationToken
    )
    {
        await using var scope = await DatabaseTransactionLeasingScope.StartTransaction(
            _dbContext,
            _options.LockTimeout,
            cancellationToken
        );

        if (!scope.Acquired)
            return;

        try
        {
            await callback(cancellationToken);
        }
        catch
        {
            scope.Abort();
            throw;
        }
    }

    private static NotSupportedException UnsupportedProviderException(string providerName) =>
        new(
            $"The current database provider '{providerName}' is not supported. "
                + "Atomizer.EntityFrameworkCore supports SQL Server, PostgreSQL and MySQL."
        );
}
