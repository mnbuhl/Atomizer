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

        Task UpdateAsync(AtomizerJob job, CancellationToken cancellationToken);

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
            AtomizerJob job,
            DateTimeOffset completedAt,
            LeaseToken leaseToken,
            CancellationToken cancellationToken
        );
        Task MarkFailedAsync(
            AtomizerJob job,
            DateTimeOffset failedAt,
            AtomizerJobError error,
            LeaseToken leaseToken,
            CancellationToken cancellationToken
        );
        Task RescheduleAsync(
            AtomizerJob job,
            DateTimeOffset visibleAt,
            AtomizerJobError? error,
            LeaseToken leaseToken,
            CancellationToken cancellationToken
        );
    }
}
