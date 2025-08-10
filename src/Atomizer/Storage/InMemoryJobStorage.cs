using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Atomizer.Abstractions;

namespace Atomizer.Storage
{
    public class InMemoryJobStorage : IJobStorage
    {
        public Task<Guid> InsertAsync(AtomizerJob job, bool enforceIdempotency, CancellationToken cancellationToken)
        {
            throw new NotImplementedException();
        }

        public Task<IReadOnlyList<AtomizerJob>> TryLeaseBatchAsync(
            QueueKey queueKey,
            int batchSize,
            DateTimeOffset now,
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
