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
    internal sealed class EntityFrameworkCoreStorage<TDbContext> : IAtomizerStorage
        where TDbContext : DbContext
    {
        private readonly TDbContext _dbContext;
        private readonly EntityFrameworkCoreJobStorageOptions _options;
        private readonly ILogger<EntityFrameworkCoreStorage<TDbContext>> _logger;

        private DbSet<AtomizerJobEntity> JobEntities => _dbContext.Set<AtomizerJobEntity>();
        private DbSet<AtomizerJobErrorEntity> JobErrorEntities => _dbContext.Set<AtomizerJobErrorEntity>();

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
    }
}
