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
    private readonly IAtomizerClock _clock;
    private readonly EntityFrameworkCoreJobStorageOptions _options;
    private readonly ILogger<EntityFrameworkCoreStorage<TDbContext>> _logger;
    private readonly RelationalProviderCache _providerCache;

    private DbSet<AtomizerJobEntity> JobEntities => _dbContext.Set<AtomizerJobEntity>();
    private DbSet<AtomizerJobErrorEntity> JobErrorEntities => _dbContext.Set<AtomizerJobErrorEntity>();
    private DbSet<AtomizerScheduleEntity> ScheduleEntities => _dbContext.Set<AtomizerScheduleEntity>();

    public EntityFrameworkCoreStorage(
        TDbContext dbContext,
        EntityFrameworkCoreJobStorageOptions options,
        IAtomizerClock clock,
        ILogger<EntityFrameworkCoreStorage<TDbContext>> logger
    )
    {
        _dbContext = dbContext;
        _options = options;
        _clock = clock;
        _logger = logger;
        _providerCache = RelationalProviderCache.Create(dbContext);
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
                return existing.Id;
            }
        }

        JobEntities.Add(entity);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return entity.Id;
    }

    public async Task UpdateAsync(AtomizerJob job, CancellationToken cancellationToken)
    {
        var updated = job.ToEntity();

        JobEntities.Update(updated);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task UpdateRangeAsync(IEnumerable<AtomizerJob> jobs, CancellationToken cancellationToken)
    {
        JobEntities.UpdateRange(jobs.Select(j => j.ToEntity()));
        return _dbContext.SaveChangesAsync(cancellationToken);
    }

    public Task<IReadOnlyList<AtomizerJob>> LeaseBatchAsync(
        QueueKey queueKey,
        int batchSize,
        DateTimeOffset now,
        TimeSpan visibilityTimeout,
        LeaseToken leaseToken,
        CancellationToken cancellationToken
    )
    {
        return Task.FromResult<IReadOnlyList<AtomizerJob>>(Array.Empty<AtomizerJob>());
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

    public async Task<int> ReleaseLeasedAsync(LeaseToken leaseToken, CancellationToken cancellationToken)
    {
        if (_providerCache is { IsSupportedProvider: true, RawSqlProvider: not null })
        {
            var sql = _providerCache.RawSqlProvider.ReleaseLeasedJobsAsync(leaseToken);
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
            entity.UpdatedAt = _clock.UtcNow;
        }

        return await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<Guid> UpsertScheduleAsync(AtomizerSchedule schedule, CancellationToken cancellationToken)
    {
        var entity = schedule.ToEntity();

        var exists = await ScheduleEntities.AsNoTracking().AnyAsync(s => s.JobKey == entity.JobKey, cancellationToken);

        if (exists)
        {
            // ScheduleEntities.Update(entity);
            // await _dbContext.SaveChangesAsync(cancellationToken);
        }
        else
        {
            ScheduleEntities.Add(entity);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return entity.Id;
    }

    public Task<IReadOnlyList<AtomizerSchedule>> LeaseDueSchedulesAsync(
        DateTimeOffset now,
        TimeSpan visibilityTimeout,
        LeaseToken leaseToken,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult<IReadOnlyList<AtomizerSchedule>>(Array.Empty<AtomizerSchedule>());
    }

    public Task<IReadOnlyList<AtomizerSchedule>> GetDueSchedulesAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        throw new NotImplementedException();
    }

    public async Task<int> ReleaseLeasedSchedulesAsync(LeaseToken leaseToken, CancellationToken cancellationToken)
    {
        if (_providerCache is { IsSupportedProvider: true, RawSqlProvider: not null })
        {
            var sql = _providerCache.RawSqlProvider.ReleaseLeasedSchedulesAsync(leaseToken);
            var result = await _dbContext.Database.ExecuteSqlInterpolatedAsync(sql, cancellationToken);
            return result;
        }

        var entities = await ScheduleEntities
            .Where(s => s.LeaseToken == leaseToken.Token)
            .ToListAsync(cancellationToken);

        foreach (var entity in entities)
        {
            entity.LeaseToken = null;
            entity.VisibleAt = null;
            entity.UpdatedAt = _clock.UtcNow;
        }

        return await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<IAtomizerLock> AcquireLockAsync(
        QueueKey queueKey,
        TimeSpan lockTimeout,
        CancellationToken cancellationToken
    )
    {
        var transaction = new DatabaseTransactionLock<TDbContext>(_dbContext, lockTimeout);
        await transaction.AcquireAsync(cancellationToken);

        return transaction;
    }
}
