using Atomizer.Abstractions;
using Atomizer.EntityFrameworkCore.Entities;
using Atomizer.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Atomizer.EntityFrameworkCore.Storage;

internal sealed class EntityFrameworkCoreStorage<TDbContext> : IAtomizerStorage
    where TDbContext : DbContext
{
    private readonly TDbContext _dbContext;
    private readonly EntityFrameworkCoreJobStorageOptions _options;
    private readonly ILogger<EntityFrameworkCoreStorage<TDbContext>> _logger;

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

    public async Task<IReadOnlyList<AtomizerJob>> LeaseBatchAsync(
        QueueKey queueKey,
        int batchSize,
        DateTimeOffset now,
        TimeSpan visibilityTimeout,
        LeaseToken leaseToken,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var candidateIds = await JobEntities
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
            .Select(j => j.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken);

        if (candidateIds.Count == 0)
        {
            _logger.LogDebug("No jobs found for queue {QueueKey} at {Now}", queueKey.Key, now);
            return Array.Empty<AtomizerJob>();
        }

        var updated = await JobEntities
            .Where(j =>
                candidateIds.Contains(j.Id)
                && (
                    j.Status == AtomizerEntityJobStatus.Pending
                        && (j.VisibleAt == null || j.VisibleAt <= now)
                        && j.ScheduledAt <= now
                    || (j.Status == AtomizerEntityJobStatus.Processing && j.VisibleAt <= now) // lease expired
                )
            )
            .ExecuteUpdateCompatAsync(
                s =>
                    s.SetProperty(j => j.Status, AtomizerEntityJobStatus.Processing)
                        .SetProperty(j => j.VisibleAt, now.Add(visibilityTimeout))
                        .SetProperty(j => j.LeaseToken, leaseToken.Token),
                cancellationToken
            );

        if (updated == 0)
        {
            _logger.LogDebug(
                "No jobs updated for queue {QueueKey} at {Now} with lease token {LeaseToken}",
                queueKey.Key,
                now,
                leaseToken
            );
            return Array.Empty<AtomizerJob>();
        }

        _logger.LogInformation(
            "Leased {Count} jobs for queue {QueueKey} with lease token {LeaseToken}",
            updated,
            queueKey.Key,
            leaseToken
        );

        var leased = await JobEntities
            .AsNoTracking()
            .Where(j =>
                candidateIds.Contains(j.Id)
                && j.Status == AtomizerEntityJobStatus.Processing
                && j.LeaseToken == leaseToken.Token
            )
            .Select(j => j.ToAtomizerJob())
            .ToListAsync(cancellationToken);

        return leased;
    }

    public async Task<int> ReleaseLeasedAsync(LeaseToken leaseToken, CancellationToken cancellationToken)
    {
        var releasedCount = await JobEntities
            .Where(j => j.LeaseToken == leaseToken.Token && j.Status == AtomizerEntityJobStatus.Processing)
            .ExecuteUpdateCompatAsync(
                s =>
                    s.SetProperty(j => j.Status, AtomizerEntityJobStatus.Pending)
                        .SetProperty(j => j.VisibleAt, _ => null)
                        .SetProperty(j => j.LeaseToken, _ => null),
                cancellationToken
            );

        return releasedCount;
    }

    public async Task<Guid> UpsertScheduleAsync(AtomizerSchedule schedule, CancellationToken cancellationToken)
    {
        var entity = schedule.ToEntity();

        var exists = await ScheduleEntities.AsNoTracking().AnyAsync(s => s.JobKey == entity.JobKey, cancellationToken);

        if (exists)
        {
            await ScheduleEntities
                .Where(s => s.JobKey == entity.JobKey)
                .ExecuteUpdateCompatAsync(
                    s =>
                        s.SetProperty(sch => sch.QueueKey, entity.QueueKey)
                            .SetProperty(sch => sch.PayloadType, entity.PayloadType)
                            .SetProperty(sch => sch.Payload, entity.Payload)
                            .SetProperty(sch => sch.Schedule, entity.Schedule)
                            .SetProperty(sch => sch.TimeZone, entity.TimeZone)
                            .SetProperty(sch => sch.MisfirePolicy, entity.MisfirePolicy)
                            .SetProperty(sch => sch.MaxCatchUp, entity.MaxCatchUp)
                            .SetProperty(sch => sch.Enabled, entity.Enabled)
                            .SetProperty(sch => sch.MaxAttempts, entity.MaxAttempts)
                            .SetProperty(sch => sch.NextRunAt, entity.NextRunAt)
                            .SetProperty(sch => sch.UpdatedAt, entity.UpdatedAt)
                            .SetProperty(sch => sch.VisibleAt, entity.VisibleAt)
                            .SetProperty(sch => sch.LeaseToken, entity.LeaseToken)
                            .SetProperty(
                                sch => sch.LastEnqueueAt,
                                sch =>
                                    sch.LastEnqueueAt > entity.LastEnqueueAt ? sch.LastEnqueueAt : entity.LastEnqueueAt
                            ),
                    cancellationToken
                );
        }
        else
        {
            ScheduleEntities.Add(entity);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }

        return entity.Id;
    }

    public async Task<IReadOnlyList<AtomizerSchedule>> LeaseDueSchedulesAsync(
        DateTimeOffset now,
        TimeSpan visibilityTimeout,
        LeaseToken leaseToken,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var candidateIds = await ScheduleEntities
            .AsNoTracking()
            .Where(s => s.NextRunAt <= now && (s.VisibleAt == null || s.VisibleAt <= now) && s.Enabled)
            .OrderBy(s => s.NextRunAt)
            .Select(s => s.Id)
            .ToListAsync(cancellationToken);

        if (candidateIds.Count == 0)
        {
            _logger.LogDebug("No due schedules found at {Now}", now);
            return Array.Empty<AtomizerSchedule>();
        }

        var updatedCount = await ScheduleEntities
            .Where(s => candidateIds.Contains(s.Id) && (s.VisibleAt == null || s.VisibleAt <= now))
            .ExecuteUpdateCompatAsync(
                s =>
                    s.SetProperty(sch => sch.VisibleAt, now.Add(visibilityTimeout))
                        .SetProperty(sch => sch.LeaseToken, leaseToken.Token),
                cancellationToken
            );

        if (updatedCount == 0)
        {
            _logger.LogDebug("No schedules updated for lease token {LeaseToken} at {Now}", leaseToken, now);
            return Array.Empty<AtomizerSchedule>();
        }

        _logger.LogInformation(
            "Leased {Count} schedules with lease token {LeaseToken} at {Now}",
            updatedCount,
            leaseToken,
            now
        );

        var leasedSchedules = await ScheduleEntities
            .AsNoTracking()
            .Where(s => candidateIds.Contains(s.Id) && s.LeaseToken == leaseToken.Token)
            .Select(s => s.ToAtomizerSchedule())
            .ToListAsync(cancellationToken);

        return leasedSchedules;
    }

    public async Task<int> ReleaseLeasedSchedulesAsync(LeaseToken leaseToken, CancellationToken cancellationToken)
    {
        var releasedCount = await ScheduleEntities
            .Where(s => s.LeaseToken == leaseToken.Token)
            .ExecuteUpdateCompatAsync(
                s => s.SetProperty(sch => sch.VisibleAt, _ => null).SetProperty(sch => sch.LeaseToken, _ => null),
                cancellationToken
            );

        return releasedCount;
    }
}
