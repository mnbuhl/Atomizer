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

        public EntityFrameworkCoreJobStorage(
            TDbContext dbContext,
            EntityFrameworkCoreJobStorageOptions options,
            IAtomizerLogger<EntityFrameworkCoreJobStorage<TDbContext>> logger
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

        public Task<IReadOnlyList<AtomizerJob>> TryLeaseBatchAsync(
            QueueKey queueKey,
            int batchSize,
            DateTimeOffset now,
            TimeSpan visibilityTimeout,
            CancellationToken cancellationToken
        )
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
    }
}
