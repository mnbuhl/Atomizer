using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Atomizer.Abstractions
{
    public interface IJobStorage
    {
        Task<Guid> InsertAsync(AtomizerJob job, bool enforceIdempotency, CancellationToken cancellationToken);
        Task<IReadOnlyList<AtomizerJob>> TryLeaseBatchAsync(
            AtomizerQueue queue,
            int batchSize,
            DateTimeOffset now,
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
