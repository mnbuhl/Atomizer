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
                job.SequenceNumber = existing.SequenceNumber;
                return existing.Id;
            }
        }

        if (job.PartitionKey != null && _providerCache is { IsSupportedProvider: true, Dialect: not null })
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

        if (job.PartitionKey != null && !_providerCache.IsSupportedProvider && _options.AllowUnsafeProviderFallback)
        {
            // LINQ fallback sequence assignment: not atomic under concurrency but safe for single-process use.
            var partitionKeyStr = job.PartitionKey.ToString();
            var queueKeyStr = job.QueueKey.Key;
            var maxSeq = await JobEntities
                .AsNoTracking()
                .Where(j => j.QueueKey == queueKeyStr && j.PartitionKey == partitionKeyStr)
                .MaxAsync(j => (long?)j.SequenceNumber, cancellationToken);
            entity.SequenceNumber = (maxSeq ?? 0L) + 1L;
            job.SequenceNumber = entity.SequenceNumber;
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
            var allForQueue = await JobEntities
                .AsNoTracking()
                .Where(j => j.QueueKey == queueKey.Key)
                .ToListAsync(cancellationToken);

            // 1) Collect blocked partitions: any partition key with a Processing job
            //    or a Pending job with prior attempts (retrying).
            var blockedPartitions = allForQueue
                .Where(j =>
                    j.PartitionKey != null
                    && (
                        j.Status == AtomizerEntityJobStatus.Processing
                        || (j.Status == AtomizerEntityJobStatus.Pending && j.Attempts > 0)
                    )
                )
                .Select(j => j.PartitionKey!)
                .ToHashSet();

            // 2) Find the lowest sequence number per unblocked partition (partition heads).
            //    Only consider Pending jobs that are due — Completed and Failed jobs must not
            //    block the next job from becoming the partition head.
            var partitionHeads = allForQueue
                .Where(j =>
                    j.PartitionKey != null
                    && !blockedPartitions.Contains(j.PartitionKey)
                    && j.Status == AtomizerEntityJobStatus.Pending
                    && (j.VisibleAt == null || j.VisibleAt <= now)
                    && j.ScheduledAt <= now
                )
                .GroupBy(j => j.PartitionKey!)
                .Select(g => g.OrderBy(j => j.SequenceNumber).First().Id)
                .ToHashSet();

            // 3) Apply eligibility filter, FIFO partition-head filter, and batch size limit.
            return allForQueue
                .Where(j =>
                    (
                        j.Status == AtomizerEntityJobStatus.Pending
                            && (j.VisibleAt == null || j.VisibleAt <= now)
                            && j.ScheduledAt <= now
                        || (j.Status == AtomizerEntityJobStatus.Processing && j.VisibleAt <= now) // lease expired
                    )
                    && (j.PartitionKey == null || partitionHeads.Contains(j.Id))
                )
                .OrderBy(j => j.ScheduledAt)
                .Take(batchSize)
                .Select(j => j.ToAtomizerJob())
                .ToList();
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
