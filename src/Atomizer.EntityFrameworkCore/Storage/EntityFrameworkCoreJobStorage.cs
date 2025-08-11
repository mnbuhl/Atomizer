using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Atomizer.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace Atomizer.EntityFrameworkCore.Storage
{
    internal sealed class EntityFrameworkCoreJobStorage<TDbContext> : IAtomizerJobStorage
        where TDbContext : DbContext
    {
        private readonly TDbContext _dbContext;
        private readonly EntityFrameworkCoreJobStorageOptions _options;
        private readonly IAtomizerLogger<EntityFrameworkCoreJobStorage<TDbContext>> _logger;

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

        public Task<Guid> InsertAsync(AtomizerJob job, bool enforceIdempotency, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
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
