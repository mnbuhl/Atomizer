using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Atomizer.Models;

namespace Atomizer.Abstractions
{
    public interface IAtomizerJobStorage
    {
        Task<Guid> InsertAsync(AtomizerJob job, bool enforceIdempotency, CancellationToken cancellationToken);
        Task<IReadOnlyList<AtomizerJob>> TryLeaseBatchAsync(
            QueueKey queueKey,
            int batchSize,
            DateTimeOffset now,
            TimeSpan visibilityTimeout,
            string leaseToken,
            CancellationToken cancellationToken
        );

        Task MarkSucceededAsync(Guid jobId, DateTimeOffset completedAt, CancellationToken cancellationToken);
        Task MarkFailedAsync(Guid jobId, Exception error, DateTimeOffset failedAt, CancellationToken cancellationToken);
        Task RescheduleAsync(
            Guid jobId,
            int attemptCount,
            DateTimeOffset visibleAt,
            CancellationToken cancellationToken
        );
        Task MoveToDeadLetterAsync(Guid jobId, string reason, CancellationToken cancellationToken);
    }
}
