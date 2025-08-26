using Atomizer.Abstractions;
using Atomizer.Core;
using Atomizer.EntityFrameworkCore.Entities;
using Atomizer.EntityFrameworkCore.Providers;
using Atomizer.Storage;
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

    public async Task UpdateJobAsync(AtomizerJob job, CancellationToken cancellationToken)
    {
        var updated = job.ToEntity();

        JobEntities.Update(updated);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task UpdateJobsAsync(IEnumerable<AtomizerJob> jobs, CancellationToken cancellationToken)
    {
        JobEntities.UpdateRange(jobs.Select(j => j.ToEntity()));
        await _dbContext.SaveChangesAsync(cancellationToken);
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
        ScheduleEntities.UpdateRange(schedules.Select(s => s.ToEntity()));
        await _dbContext.SaveChangesAsync(cancellationToken);
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

    public async Task<IAtomizerLock> AcquireLockAsync(
        QueueKey queueKey,
        TimeSpan lockTimeout,
        CancellationToken cancellationToken
    )
    {
        if (_providerCache.DatabaseProvider == DatabaseProvider.Unknown)
        {
            return new NoopLock();
        }

        var transaction = new DatabaseTransactionLock<TDbContext>(_dbContext, lockTimeout);
        await transaction.AcquireAsync(cancellationToken);

        return transaction;
    }
}
