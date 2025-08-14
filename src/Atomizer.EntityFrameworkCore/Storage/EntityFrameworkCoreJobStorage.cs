using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomizer.Abstractions;
using Atomizer.EntityFrameworkCore.Entities;
using Atomizer.Models;
using Microsoft.EntityFrameworkCore;

namespace Atomizer.EntityFrameworkCore.Storage
{
    internal sealed class EntityFrameworkCoreJobStorage<TDbContext> : IAtomizerJobStorage
        where TDbContext : DbContext
    {
        private readonly TDbContext _dbContext;
        private readonly EntityFrameworkCoreJobStorageOptions _options;
        private readonly IAtomizerLogger<EntityFrameworkCoreJobStorage<TDbContext>> _logger;

        private DbSet<AtomizerJobEntity> JobEntities => _dbContext.Set<AtomizerJobEntity>();

        private readonly EFCoreStorageProvider _storageProvider;

        public EntityFrameworkCoreJobStorage(
            TDbContext dbContext,
            EntityFrameworkCoreJobStorageOptions options,
            IAtomizerLogger<EntityFrameworkCoreJobStorage<TDbContext>> logger
        )
        {
            _dbContext = dbContext;
            _options = options;
            _logger = logger;

            _storageProvider = GetStorageProvider(dbContext);
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
                .ExecuteUpdateAsync(
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

        public Task<int> ReleaseLeasedAsync(string leaseToken, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task MarkSucceededAsync(Guid jobId, DateTimeOffset completedAt, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task MarkFailedAsync(
            Guid jobId,
            Exception error,
            DateTimeOffset failedAt,
            CancellationToken cancellationToken
        )
        {
            throw new NotImplementedException();
        }

        public Task RescheduleAsync(
            Guid jobId,
            int attemptCount,
            DateTimeOffset visibleAt,
            CancellationToken cancellationToken
        )
        {
            throw new NotImplementedException();
        }

        public Task MoveToDeadLetterAsync(Guid jobId, string reason, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        private static EFCoreStorageProvider GetStorageProvider(TDbContext dbContext)
        {
            var provider = dbContext.Database.ProviderName;

            return provider switch
            {
                "Npgsql.EntityFrameworkCore.PostgreSQL" => EFCoreStorageProvider.PostgreSql,
                "Pomelo.EntityFrameworkCore.MySql" or "MySql.Data.EntityFrameworkCore" or "MySql.EntityFrameworkCore" =>
                    EFCoreStorageProvider.MySql,
                "Microsoft.EntityFrameworkCore.SqlServer" => EFCoreStorageProvider.SqlServer,
                _ => throw new NotSupportedException($"Provider '{provider}' is not supported."),
            };
        }
    }
}
