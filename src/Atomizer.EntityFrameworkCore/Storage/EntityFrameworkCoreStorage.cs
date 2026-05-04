using Atomizer.Abstractions;
using Atomizer.Core;
using Atomizer.EntityFrameworkCore.Entities;
using Atomizer.EntityFrameworkCore.Providers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Atomizer.EntityFrameworkCore.Storage;

internal sealed class EntityFrameworkCoreStorage<TDbContext> : IAtomizerStorage, IAtomizerHeartbeatRecoveryStorage
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
    }

    public void ValidateHeartbeatRecoverySupport() { }

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
        ValidateHeartbeatRecoverySupport();

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

        if (_providerCache is { IsSupportedProvider: true, Dialect: not null })
        {
            var sql = _providerCache.Dialect.GetDueJobs(queueKey, now, batchSize);

            var entities = await JobEntities.FromSqlInterpolated(sql).AsNoTracking().ToListAsync(cancellationToken);

            return entities.Select(job => job.ToAtomizerJob()).ToList();
        }

        if (!_providerCache.IsSupportedProvider && _options.AllowUnsafeProviderFallback)
        {
            // WARNING: AsNoTracking() with no row lock means two concurrent QueuePumps
            // on the same process (or any second node) will both receive the same jobs.
            // AllowUnsafeProviderFallback is only safe with DegreeOfParallelism=1 and
            // a single process instance. It is not safe for production use.
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
        if (_providerCache is { IsSupportedProvider: true, Dialect: not null })
        {
            var sql = _providerCache.Dialect.ReleaseLeasedJobs(leaseToken, now);
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

        if (_providerCache is { IsSupportedProvider: true, Dialect: not null })
        {
            var now = _clock.UtcNow;
            var sql = _providerCache.Dialect.UpsertScheduleAsync(schedule, now);
            await _dbContext.Database.ExecuteSqlInterpolatedAsync(sql, cancellationToken);
            return await ScheduleEntities
                .Where(s => s.JobKey == entity.JobKey)
                .Select(s => s.Id)
                .FirstAsync(cancellationToken);
        }

        if (!_providerCache.IsSupportedProvider && _options.AllowUnsafeProviderFallback)
        {
            // Not race-safe - use only with 1 service running
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
                _logger.LogError(ex, "Failed to upsert schedule for job {JobKey}", schedule.JobKey);
            }

            return entity.Id;
        }

        throw new NotSupportedException(
            "The current database provider is not supported. "
                + "To bypass this check, set AllowUnsafeProviderFallback to true in EntityFrameworkCoreJobStorageOptions. "
                + "Note that this may lead to unexpected behavior."
        );
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
            throw;
        }
    }

    public async Task<IReadOnlyList<AtomizerSchedule>> GetDueSchedulesAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (_providerCache is { IsSupportedProvider: true, Dialect: not null })
        {
            var sql = _providerCache.Dialect.GetDueSchedules(now);

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
}
