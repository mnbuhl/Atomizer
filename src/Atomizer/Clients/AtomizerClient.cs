using System;
using System.Threading;
using System.Threading.Tasks;
using Atomizer.Abstractions;
using Atomizer.Hosting;
using Atomizer.Models;
using Microsoft.Extensions.Logging;

namespace Atomizer.Clients
{
    public class AtomizerClient : IAtomizerClient
    {
        private readonly IAtomizerStorage _storage;
        private readonly IAtomizerJobSerializer _jobSerializer;
        private readonly IAtomizerClock _clock;
        private readonly ILogger<AtomizerClient> _logger;

        public AtomizerClient(
            IAtomizerStorage storage,
            IAtomizerJobSerializer jobSerializer,
            IAtomizerClock clock,
            ILogger<AtomizerClient> logger
        )
        {
            _storage = storage;
            _jobSerializer = jobSerializer;
            _clock = clock;
            _logger = logger;
        }

        public Task<Guid> EnqueueAsync<TPayload>(
            TPayload payload,
            Action<EnqueueOptions>? configure = null,
            CancellationToken cancellation = default
        )
        {
            var options = new EnqueueOptions();
            configure?.Invoke(options);

            return EnqueueInternalAsync(payload, _clock.UtcNow, options, cancellation);
        }

        public Task<Guid> ScheduleAsync<TPayload>(
            TPayload payload,
            DateTimeOffset runAt,
            Action<EnqueueOptions>? configure = null,
            CancellationToken cancellation = default
        )
        {
            var options = new EnqueueOptions();
            configure?.Invoke(options);

            return EnqueueInternalAsync(payload, runAt, options, cancellation);
        }

        private async Task<Guid> EnqueueInternalAsync<TPayload>(
            TPayload payload,
            DateTimeOffset when,
            EnqueueOptions options,
            CancellationToken ct
        )
        {
            var serializedPayload = _jobSerializer.Serialize(payload);

            var job = AtomizerJob.Create(
                options.Queue,
                options.TypeOverride ?? typeof(TPayload),
                serializedPayload,
                _clock.UtcNow,
                when,
                options.MaxAttempts,
                options.IdempotencyKey
            );

            var jobId = await _storage.InsertAsync(job, ct);

            _logger.LogDebug(
                "Enqueuing job {JobId} with payload type {PayloadType} to queue {QueueKey} at {ScheduledAt}",
                jobId,
                job.PayloadType.FullName,
                job.QueueKey,
                job.ScheduledAt
            );

            return jobId;
        }
    }
}
