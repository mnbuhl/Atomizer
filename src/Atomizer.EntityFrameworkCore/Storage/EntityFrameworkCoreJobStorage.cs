using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomizer.Abstractions;
using Atomizer.EntityFrameworkCore.Entities;
using Atomizer.EntityFrameworkCore.Extensions;
using Atomizer.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Atomizer.EntityFrameworkCore.Storage
{
    internal sealed class EntityFrameworkCoreJobStorage<TDbContext> : IAtomizerJobStorage
        where TDbContext : DbContext
    {
        private readonly TDbContext _dbContext;
        private readonly EntityFrameworkCoreJobStorageOptions _options;
        private readonly ILogger<EntityFrameworkCoreJobStorage<TDbContext>> _logger;

        private DbSet<AtomizerJobEntity> JobEntities => _dbContext.Set<AtomizerJobEntity>();
        private DbSet<AtomizerJobErrorEntity> JobErrorEntities => _dbContext.Set<AtomizerJobErrorEntity>();

        public EntityFrameworkCoreJobStorage(
            TDbContext dbContext,
            EntityFrameworkCoreJobStorageOptions options,
            ILogger<EntityFrameworkCoreJobStorage<TDbContext>> logger
        )
        {
            _dbContext = dbContext;
            _options = options;
            _logger = logger;
        }

        public async Task<Guid> InsertAsync(
            AtomizerJob job,
            bool enforceIdempotency,
            CancellationToken cancellationToken
        )
        {
            // Idempotency (simple lookup; relies on app-level uniqueness of IdempotencyKey)
            if (enforceIdempotency && !string.IsNullOrWhiteSpace(job.IdempotencyKey))
            {
                var existingId = await JobEntities
                    .Where(j => j.IdempotencyKey == job.IdempotencyKey)
                    .Select(j => j.Id)
                    .FirstOrDefaultAsync(cancellationToken);

                if (existingId != null && existingId != Guid.Empty)
                {
                    _logger.LogInformation(
                        "Insert idempotent-hit for key {Key} -> {JobId}",
                        job.IdempotencyKey,
                        existingId
                    );
                    return existingId;
                }
            }

            var entity = job.ToEntity();
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

        public async Task<IReadOnlyList<AtomizerJob>> TryLeaseBatchAsync(
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

        public async Task MarkCompletedAsync(
            AtomizerJob job,
            DateTimeOffset completedAt,
            LeaseToken leaseToken,
            CancellationToken cancellationToken
        )
        {
            var updated = await JobEntities
                .Where(j => j.Id == job.Id && j.LeaseToken == leaseToken.Token)
                .ExecuteUpdateCompatAsync(
                    s =>
                        s.SetProperty(j => j.Status, AtomizerEntityJobStatus.Completed)
                            .SetProperty(j => j.CompletedAt, completedAt)
                            .SetProperty(j => j.Attempts, job.Attempts)
                            .SetProperty(j => j.VisibleAt, _ => null)
                            .SetProperty(j => j.LeaseToken, _ => null),
                    cancellationToken
                );

            if (updated == 0)
            {
                _logger.LogWarning(
                    "Failed to mark job {JobId} as completed with lease token {LeaseToken}. Job may not exist or lease token mismatch",
                    job.Id,
                    leaseToken
                );
            }
            else
            {
                _logger.LogDebug("Job {JobId} marked as completed with lease token {LeaseToken}", job.Id, leaseToken);
            }
        }

        public async Task MarkFailedAsync(
            AtomizerJob job,
            DateTimeOffset failedAt,
            AtomizerJobError error,
            LeaseToken leaseToken,
            CancellationToken cancellationToken
        )
        {
            var affected = await JobEntities
                .Where(j => j.Id == job.Id && j.LeaseToken == leaseToken.Token)
                .ExecuteUpdateCompatAsync(
                    set =>
                        set.SetProperty(j => j.Status, AtomizerEntityJobStatus.Failed)
                            .SetProperty(j => j.Attempts, job.Attempts)
                            .SetProperty(j => j.FailedAt, failedAt)
                            .SetProperty(j => j.VisibleAt, _ => null)
                            .SetProperty(j => j.LeaseToken, _ => null),
                    cancellationToken
                );

            JobErrorEntities.Add(error.ToEntity());
            await _dbContext.SaveChangesAsync(cancellationToken);

            if (affected == 0)
            {
                _logger.LogError(
                    "Failed to mark job {JobId} as failed with lease token {LeaseToken}. Job may not exist",
                    job.Id,
                    leaseToken
                );
            }
            else
            {
                _logger.LogInformation(
                    "Job {JobId} marked as failed with lease token {LeaseToken}",
                    job.Id,
                    leaseToken
                );
            }
        }

        public async Task RescheduleAsync(
            AtomizerJob job,
            DateTimeOffset visibleAt,
            AtomizerJobError? error,
            LeaseToken leaseToken,
            CancellationToken cancellationToken
        )
        {
            var updated = await JobEntities
                .Where(j => j.Id == job.Id && j.LeaseToken == leaseToken.Token)
                .ExecuteUpdateCompatAsync(
                    s =>
                        s.SetProperty(j => j.Status, AtomizerEntityJobStatus.Pending)
                            .SetProperty(j => j.Attempts, job.Attempts)
                            .SetProperty(j => j.VisibleAt, visibleAt)
                            .SetProperty(j => j.LeaseToken, _ => null),
                    cancellationToken
                );

            if (error != null)
            {
                JobErrorEntities.Add(error.ToEntity());
                await _dbContext.SaveChangesAsync(cancellationToken);
            }

            if (updated == 0)
            {
                _logger.LogWarning(
                    "Failed to reschedule job {JobId} with lease token {LeaseToken}. Job may not exist or lease token mismatch",
                    job.Id,
                    leaseToken
                );
            }
            else
            {
                _logger.LogDebug(
                    "Job {JobId} rescheduled with lease token {LeaseToken} for visibility at {VisibleAt}",
                    job.Id,
                    leaseToken,
                    visibleAt
                );
            }
        }
    }
}
