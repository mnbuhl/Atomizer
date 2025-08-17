using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Atomizer.Models;

namespace Atomizer.Abstractions
{
    public interface IAtomizerStorage
    {
        Task<Guid> InsertAsync(AtomizerJob job, bool enforceIdempotency, CancellationToken cancellationToken);

        Task UpdateAsync(AtomizerJob job, CancellationToken cancellationToken);

        Task<IReadOnlyList<AtomizerJob>> LeaseBatchAsync(
            QueueKey queueKey,
            int batchSize,
            DateTimeOffset now,
            TimeSpan visibilityTimeout,
            LeaseToken leaseToken,
            CancellationToken cancellationToken
        );

        Task<int> ReleaseLeasedAsync(LeaseToken leaseToken, CancellationToken cancellationToken);

        Task<IReadOnlyList<AtomizerRecurringJob>> LeaseDueRecurringAsync(
            QueueKey queueKey,
            DateTimeOffset now,
            LeaseToken leaseToken,
            CancellationToken cancellationToken
        );

        Task<int> ReleaseLeasedRecurringAsync(LeaseToken leaseToken, CancellationToken cancellationToken);
    }
}
