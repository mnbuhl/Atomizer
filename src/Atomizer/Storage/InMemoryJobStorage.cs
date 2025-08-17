using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomizer.Abstractions;
using Atomizer.Models;
using Microsoft.Extensions.Logging;

namespace Atomizer.Storage
{
    internal sealed class InMemoryJobStorage : IAtomizerJobStorage
    {
        // Global store of jobs
        private readonly ConcurrentDictionary<Guid, AtomizerJob> _jobs = new ConcurrentDictionary<Guid, AtomizerJob>();

        // Optional idempotency index: idempotencyKey -> jobId
        private readonly ConcurrentDictionary<string, Guid> _idempotency = new ConcurrentDictionary<string, Guid>(
            StringComparer.Ordinal
        );

        // Single process-wide lock to ensure atomic leasing batches.
        private readonly object _leaseGate = new object();

        // Gate for insert+evict to keep the index and maps consistent
        private readonly object _insertEvictGate = new object();

        // Tracks insertion order for eviction; guarded by _insertEvictGate.
        private readonly Queue<Guid> _insertionOrder = new Queue<Guid>();

        // Options for configuring the in-memory job storage
        private readonly InMemoryJobStorageOptions _options;
        private readonly ILogger<InMemoryJobStorage> _logger;

        public InMemoryJobStorage(InMemoryJobStorageOptions options, ILogger<InMemoryJobStorage> logger)
        {
            _options = options;
            _logger = logger;
        }

        public Task<Guid> InsertAsync(AtomizerJob job, bool enforceIdempotency, CancellationToken cancellationToken)
        {
            lock (_insertEvictGate)
            {
                if (enforceIdempotency && !string.IsNullOrWhiteSpace(job.IdempotencyKey))
                {
                    if (_idempotency.TryGetValue(job.IdempotencyKey, out var existing))
                    {
                        _logger.LogInformation(
                            "Idempotent hit for key '{Key}' -> {JobId}",
                            job.IdempotencyKey,
                            existing
                        );
                        return Task.FromResult(existing);
                    }
                }

                if (!_jobs.TryAdd(job.Id, job))
                {
                    throw new InvalidOperationException($"Job with Id {job.Id} already exists.");
                }

                _insertionOrder.Enqueue(job.Id);

                if (enforceIdempotency && !string.IsNullOrWhiteSpace(job.IdempotencyKey))
                {
                    _idempotency.TryAdd(job.IdempotencyKey, job.Id);
                }

                _logger.LogDebug(
                    "Inserted job {JobId} into '{Queue}' (count={Count})",
                    job.Id,
                    job.QueueKey,
                    _jobs.Count
                );

                EvictWhileOverCapacity(_options.MaximumJobsInMemory);
            }

            return Task.FromResult(job.Id);
        }

        public Task UpdateAsync(AtomizerJob job, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            _jobs[job.Id] = job; // Update the job in the dictionary

            _logger.LogDebug("Updated job {JobId} in '{Queue}'", job.Id, job.QueueKey);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AtomizerJob>> TryLeaseBatchAsync(
            QueueKey queueKey,
            int batchSize,
            DateTimeOffset now,
            TimeSpan visibilityTimeout,
            LeaseToken leaseToken,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            List<AtomizerJob> leased;
            lock (_leaseGate)
            {
                // Select eligible jobs atomically under the gate
                leased = _jobs
                    .Values.Where(j =>
                        j.QueueKey == queueKey
                        && j.Status == AtomizerJobStatus.Pending
                        && (j.VisibleAt == null || j.VisibleAt <= now)
                        && j.ScheduledAt <= now
                    )
                    .OrderBy(j => j.ScheduledAt)
                    .Take(Math.Max(0, batchSize))
                    .ToList();

                if (leased.Count == 0)
                    return Task.FromResult((IReadOnlyList<AtomizerJob>)Array.Empty<AtomizerJob>());

                // Flip to Processing, set visibility, increment attempt — all under the same lock
                foreach (var job in leased)
                {
                    if (_jobs.TryGetValue(job.Id, out var stored))
                    {
                        stored.Status = AtomizerJobStatus.Processing;
                        stored.VisibleAt = now + visibilityTimeout;
                        stored.LeaseToken = leaseToken;
                        // reflect mutations in the dictionary (stored is a reference type)
                        _jobs[job.Id] = stored;
                    }
                }
            }

            _logger.LogDebug("Leased {Count} job(s) from '{Queue}'", leased.Count, queueKey);
            // Return the leased snapshots
            return Task.FromResult((IReadOnlyList<AtomizerJob>)leased);
        }

        public Task<int> ReleaseLeasedAsync(LeaseToken leaseToken, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int releasedCount = 0;

            lock (_leaseGate)
            {
                foreach (
                    var job in _jobs.Values.Where(j =>
                        j.LeaseToken?.Token == leaseToken.Token && j.Status == AtomizerJobStatus.Processing
                    )
                )
                {
                    job.Status = AtomizerJobStatus.Pending;
                    job.VisibleAt = null; // Clear visibility to make it available immediately
                    job.LeaseToken = null; // Clear lease token
                    _jobs[job.Id] = job; // Update the job in the dictionary
                    releasedCount++;
                }
            }

            _logger.LogDebug("Released {Count} leased job(s) with token '{LeaseToken}'", releasedCount, leaseToken);
            return Task.FromResult(releasedCount);
        }

        private void EvictWhileOverCapacity(int max)
        {
            while (_jobs.Count > max && _insertionOrder.Count > 0)
            {
                var id = _insertionOrder.Dequeue();

                // if already removed (e.g., completed & later evicted), continue
                if (_jobs.TryRemove(id, out var job))
                {
                    // Remove from idempotency index if it exists
                    if (!string.IsNullOrWhiteSpace(job.IdempotencyKey))
                    {
                        _idempotency.TryRemove(job.IdempotencyKey, out _);
                    }

                    _logger.LogInformation(
                        "Evicted job {JobId} due to capacity limit ({Current}/{Max})",
                        id,
                        _jobs.Count,
                        max
                    );
                }
            }
        }
    }
}
