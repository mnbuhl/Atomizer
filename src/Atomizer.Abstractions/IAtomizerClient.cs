using System;
using System.Threading;
using System.Threading.Tasks;

namespace Atomizer.Abstractions
{
    public interface IAtomizerClient
    {
        Task<Guid> EnqueueAsync<TPayload>(
            TPayload payload,
            Action<EnqueueOptions>? configure = null,
            CancellationToken cancellation = default
        );

        Task<Guid> ScheduleAsync<TPayload>(
            TPayload payload,
            DateTimeOffset runAt,
            Action<EnqueueOptions>? configure = null,
            CancellationToken cancellation = default
        );
    }

    public sealed class EnqueueOptions
    {
        /// <summary>
        /// The queue to which the job will be added. Defaults to AtomizerQueue.Default.
        /// </summary>
        public string Queue { get; set; } = AtomizerQueue.Default;

        /// <summary>
        /// Enqueue the job with a different type than the registered type.
        /// </summary>
        public Type? TypeOverride { get; set; }

        /// <summary>
        /// The FIFO key for FIFO queues, used to group jobs.
        /// </summary>
        public string? FifoKey { get; set; }

        /// <summary>
        /// The idempotency key for the job, used to ensure that duplicate jobs are not processed.
        /// </summary>
        public string? IdempotencyKey { get; set; }
    }
}
