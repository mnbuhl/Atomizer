using System;
using System.Threading;
using System.Threading.Tasks;
using Atomizer.Abstractions;
using Atomizer.Models;

// ReSharper disable once CheckNamespace
namespace Atomizer
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
        public string Queue { get; set; } = QueueKey.Default;

        /// <summary>
        /// Enqueue the job with a different type than the registered type.
        /// </summary>
        public Type? TypeOverride { get; set; }

        /// <summary>
        /// The idempotency key for the job, used to ensure that duplicate jobs are not processed.
        /// </summary>
        public string? IdempotencyKey { get; set; }

        /// <summary>
        /// The correlation ID for the job, used to group related jobs together.
        /// </summary>
        public string? CorrelationId { get; set; }

        /// <summary>
        /// The causation ID for the job, used to track the origin of the job.
        /// </summary>
        public string? CausationId { get; set; }
    }
}
