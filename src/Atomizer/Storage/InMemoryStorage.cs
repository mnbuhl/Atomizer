using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Atomizer.Abstractions;
using Microsoft.Extensions.Logging;

namespace Atomizer.Storage
{
    internal sealed class InMemoryStorage : IAtomizerStorage
    {
        // Global store of jobs
        private readonly ConcurrentDictionary<Guid, AtomizerJob> _jobs = new ConcurrentDictionary<Guid, AtomizerJob>();
        private readonly ConcurrentDictionary<JobKey, AtomizerSchedule> _schedules =
            new ConcurrentDictionary<JobKey, AtomizerSchedule>();

        // Single process-wide lock to ensure atomic leasing batches.
        private readonly object _leaseGate = new object();

        // Gate for insert+evict to keep the index and maps consistent
        private readonly object _insertEvictGate = new object();

        // Tracks insertion order for eviction; guarded by _insertEvictGate.
        private readonly Queue<Guid> _insertionOrder = new Queue<Guid>();

        // Options for configuring the in-memory job storage
        private readonly InMemoryJobStorageOptions _options;
        private readonly ILogger<InMemoryStorage> _logger;

        public InMemoryStorage(InMemoryJobStorageOptions options, ILogger<InMemoryStorage> logger)
        {
            _options = options;
            _logger = logger;
        }

        public Task<Guid> InsertAsync(AtomizerJob job, CancellationToken cancellationToken)
        {
            lock (_insertEvictGate)
            {
                if (!_jobs.TryAdd(job.Id, job))
                {
                    throw new InvalidOperationException($"Job with Id {job.Id} already exists.");
                }

                _insertionOrder.Enqueue(job.Id);

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

        public Task<IReadOnlyList<AtomizerJob>> LeaseBatchAsync(
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

        public Task<Guid> UpsertScheduleAsync(AtomizerSchedule schedule, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var key = schedule.JobKey;

            if (_schedules.TryGetValue(key, out var existingSchedule))
            {
                // Update existing schedule
                existingSchedule.NextRunAt = schedule.NextRunAt;
                existingSchedule.Schedule = schedule.Schedule;
                existingSchedule.UpdatedAt = DateTimeOffset.UtcNow;
                existingSchedule.MaxAttempts = schedule.MaxAttempts;
                existingSchedule.Payload = schedule.Payload;
                existingSchedule.PayloadType = schedule.PayloadType;
                existingSchedule.QueueKey = schedule.QueueKey;
                existingSchedule.MisfirePolicy = schedule.MisfirePolicy;
                existingSchedule.Enabled = schedule.Enabled;
                _logger.LogDebug("Updated existing schedule for {JobKey}", key);
            }
            else
            {
                // Insert new schedule
                if (!_schedules.TryAdd(key, schedule))
                {
                    throw new InvalidOperationException($"Schedule for {key} already exists.");
                }
                _logger.LogDebug("Inserted new schedule for {JobKey}", key);
            }

            return Task.FromResult(schedule.Id);
        }

        public Task<IReadOnlyList<AtomizerSchedule>> LeaseDueSchedulesAsync(
            DateTimeOffset now,
            TimeSpan visibilityTimeout,
            LeaseToken leaseToken,
            CancellationToken cancellationToken
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            // Find all schedules that are due and not yet leased
            var dueSchedules = _schedules
                .Values.Where(s => s.NextRunAt <= now && (s.VisibleAt == null || s.VisibleAt <= now) && s.Enabled)
                .OrderBy(s => s.NextRunAt)
                .ToList();

            if (dueSchedules.Count == 0)
            {
                _logger.LogDebug("No due schedules found at {Now}", now);
                return Task.FromResult((IReadOnlyList<AtomizerSchedule>)Array.Empty<AtomizerSchedule>());
            }

            // Lease the schedules by setting their visibility and lease token
            foreach (var schedule in dueSchedules)
            {
                schedule.VisibleAt = now + visibilityTimeout;
                schedule.LeaseToken = leaseToken;
                _logger.LogDebug("Leased schedule for {JobKey} until {VisibleAt}", schedule.JobKey, schedule.VisibleAt);
            }

            return Task.FromResult((IReadOnlyList<AtomizerSchedule>)dueSchedules);
        }

        public Task<int> ReleaseLeasedSchedulesAsync(LeaseToken leaseToken, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            int releasedCount = 0;

            // Release schedules by clearing their lease token and visibility
            foreach (var schedule in _schedules.Values.Where(s => s.LeaseToken?.Token == leaseToken.Token))
            {
                schedule.VisibleAt = null; // Clear visibility to make it available immediately
                schedule.LeaseToken = null; // Clear lease token
                releasedCount++;
                _logger.LogDebug(
                    "Released schedule for {JobKey} with token '{LeaseToken}'",
                    schedule.JobKey,
                    leaseToken
                );
            }

            return Task.FromResult(releasedCount);
        }

        private void EvictWhileOverCapacity(int max)
        {
            while (_jobs.Count > max && _insertionOrder.Count > 0)
            {
                var id = _insertionOrder.Dequeue();

                // if already removed (e.g., completed & later evicted), continue
                if (_jobs.TryRemove(id, out _))
                {
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
