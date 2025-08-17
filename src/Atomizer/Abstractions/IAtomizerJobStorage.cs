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
            LeaseToken leaseToken,
            CancellationToken cancellationToken
        );

        Task<int> ReleaseLeasedAsync(LeaseToken leaseToken, CancellationToken cancellationToken);

        Task MarkCompletedAsync(
            Guid jobId,
            LeaseToken leaseToken,
            DateTimeOffset completedAt,
            CancellationToken cancellationToken
        );
        Task MarkFailedAsync(
            Guid jobId,
            LeaseToken leaseToken,
            Exception error,
            DateTimeOffset failedAt,
            CancellationToken cancellationToken
        );
        Task RescheduleAsync(
            Guid jobId,
            LeaseToken leaseToken,
            int attemptCount,
            DateTimeOffset visibleAt,
            CancellationToken cancellationToken
        );
    }
}
