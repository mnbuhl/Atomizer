using System;
using System.Threading;
using System.Threading.Tasks;
using Atomizer.Abstractions;
using Atomizer.Hosting;

namespace Atomizer.Client
{
    public class AtomizerClient : IAtomizerClient
    {
        private readonly IAtomizerJobStorage _jobStorage;
        private readonly IAtomizerJobSerializer _jobSerializer;
        private readonly IAtomizerClock _clock;
        private readonly IAtomizerLogger<AtomizerClient> _logger;

        public AtomizerClient(
            IAtomizerJobStorage jobStorage,
            IAtomizerJobSerializer jobSerializer,
            IAtomizerClock clock,
            IAtomizerLogger<AtomizerClient> logger
        )
        {
            _jobStorage = jobStorage;
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

        private Task<Guid> EnqueueInternalAsync<TPayload>(
            TPayload payload,
            DateTimeOffset when,
            EnqueueOptions options,
            CancellationToken ct
        )
        {
            var serializedPayload = _jobSerializer.Serialize(payload);
            var job = new AtomizerJob
            {
                Id = Guid.NewGuid(),
                QueueKey = options.Queue,
                PayloadType = options.TypeOverride ?? typeof(TPayload),
                Payload = serializedPayload,
                ScheduledAt = when,
                VisibleAt = null,
                Status = AtomizerJobStatus.Pending,
                Attempt = 0,
                CorrelationId = options.CorrelationId,
                CausationId = options.CorrelationId,
                IdempotencyKey = options.IdempotencyKey,
                CreatedAt = _clock.UtcNow,
            };

            var enforceIdem = !string.IsNullOrEmpty(job.IdempotencyKey);

            _logger.LogDebug(
                "Enqueuing job {JobId} with payload type {PayloadType} to queue {QueueKey} at {ScheduledAt}",
                job.Id,
                job.PayloadType.FullName,
                job.QueueKey,
                job.ScheduledAt
            );

            return _jobStorage.InsertAsync(job, enforceIdem, ct);
        }
    }
}
