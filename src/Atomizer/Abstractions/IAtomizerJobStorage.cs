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

        Task<int> ReleaseLeasedAsync(string leaseToken, CancellationToken cancellationToken);

        Task MarkCompletedAsync(
            Guid jobId,
            string leaseToken,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken
        );
        Task MarkFailedAsync(
            Guid jobId,
            string leaseToken,
            Exception error,
            DateTimeOffset failedAt,
            CancellationToken cancellationToken
        );
        Task RescheduleAsync(
            Guid jobId,
            string leaseToken,
            int attemptCount,
            DateTimeOffset visibleAt,
            CancellationToken cancellationToken
        );
    }
}
