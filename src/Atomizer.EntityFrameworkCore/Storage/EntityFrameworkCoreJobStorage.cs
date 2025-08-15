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
            cancellationToken.ThrowIfCancellationRequested();

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

        public async Task<IReadOnlyList<AtomizerJob>> TryLeaseBatchAsync(
            QueueKey queueKey,
            int batchSize,
            DateTimeOffset now,
            TimeSpan visibilityTimeout,
            string leaseToken,
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
                .ThenBy(j => j.CreatedAt)
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
                            .SetProperty(j => j.Attempt, j => j.Attempt + 1)
                            .SetProperty(j => j.VisibleAt, now.Add(visibilityTimeout))
                            .SetProperty(j => j.LeaseToken, leaseToken),
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
                    && j.LeaseToken == leaseToken
                )
                .Select(j => j.ToAtomizerJob())
                .ToListAsync(cancellationToken);

            return leased;
        }

        public async Task<int> ReleaseLeasedAsync(string leaseToken, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var releasedCount = await JobEntities
                .Where(j => j.LeaseToken == leaseToken && j.Status == AtomizerEntityJobStatus.Processing)
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
            Guid jobId,
            string leaseToken,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            var updated = await JobEntities
                .Where(j => j.Id == jobId && j.LeaseToken == leaseToken)
                .ExecuteUpdateCompatAsync(
                    s =>
                        s.SetProperty(j => j.Status, AtomizerEntityJobStatus.Completed)
                            .SetProperty(j => j.CompletedAt, completedAt)
                            .SetProperty(j => j.VisibleAt, _ => null)
                            .SetProperty(j => j.LeaseToken, _ => null),
                    cancellationToken
                );

            if (updated == 0)
            {
                _logger.LogWarning(
                    "Failed to mark job {JobId} as completed with lease token {LeaseToken}. Job may not exist or lease token mismatch",
                    jobId,
                    leaseToken
                );
            }
            else
            {
                _logger.LogDebug("Job {JobId} marked as completed with lease token {LeaseToken}", jobId, leaseToken);
            }
        }

        public async Task MarkFailedAsync(
            Guid jobId,
            string leaseToken,
            Exception error,
            DateTimeOffset failedAt,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            var affected = await JobEntities
                .Where(j => j.Id == jobId)
                .ExecuteUpdateCompatAsync(
                    set =>
                        set.SetProperty(j => j.Status, _ => AtomizerEntityJobStatus.Failed)
                            .SetProperty(j => j.FailedAt, _ => failedAt)
                            .SetProperty(j => j.VisibleAt, _ => null)
                            .SetProperty(j => j.LeaseToken, _ => null),
                    cancellationToken
                );

            if (affected == 0)
            {
                _logger.LogWarning(
                    "Failed to mark job {JobId} as failed with lease token {LeaseToken}. Job may not exist",
                    jobId,
                    leaseToken
                );
            }
            else
            {
                _logger.LogError(
                    error,
                    "Job {JobId} marked as failed with lease token {LeaseToken}",
                    jobId,
                    leaseToken
                );
            }
        }

        public async Task RescheduleAsync(
            Guid jobId,
            string leaseToken,
            int attemptCount,
            DateTimeOffset visibleAt,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            var updated = await JobEntities
                .Where(j => j.Id == jobId && j.LeaseToken == leaseToken)
                .ExecuteUpdateCompatAsync(
                    s =>
                        s.SetProperty(j => j.Status, AtomizerEntityJobStatus.Pending)
                            .SetProperty(j => j.Attempt, attemptCount)
                            .SetProperty(j => j.VisibleAt, visibleAt)
                            .SetProperty(j => j.LeaseToken, _ => null),
                    cancellationToken
                );

            if (updated == 0)
            {
                _logger.LogWarning(
                    "Failed to reschedule job {JobId} with lease token {LeaseToken}. Job may not exist or lease token mismatch",
                    jobId,
                    leaseToken
                );
            }
            else
            {
                _logger.LogDebug(
                    "Job {JobId} rescheduled with lease token {LeaseToken} for visibility at {VisibleAt}",
                    jobId,
                    leaseToken,
                    visibleAt
                );
            }
        }
    }
}
