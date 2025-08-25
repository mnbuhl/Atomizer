using System.Collections.Concurrent;
using Atomizer.Abstractions;
using Atomizer.Core;
using Microsoft.Extensions.Logging;

namespace Atomizer.Storage;

public sealed class InMemoryStorage : IAtomizerStorage
{
    private readonly ConcurrentDictionary<Guid, AtomizerJob> _jobs = new();
    private readonly Dictionary<QueueKey, HashSet<Guid>> _queues = new(); // guarded per-queue
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, byte>> _leasesByToken = new();

    // Locks
    private readonly ConcurrentDictionary<QueueKey, object> _queueLocks = new();
    private readonly object _schedulesSync = new();

    // Schedules (single lock)
    private readonly Dictionary<JobKey, AtomizerSchedule> _schedules = new();

    private readonly InMemoryJobStorageOptions _options;
    private readonly IAtomizerClock _clock;
    private readonly ILogger<InMemoryStorage> _logger;

    public InMemoryStorage(InMemoryJobStorageOptions options, IAtomizerClock clock, ILogger<InMemoryStorage> logger)
    {
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    public Task<Guid> InsertAsync(AtomizerJob job, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Evict(); // opportunistic global pass; uses per-queue locks internally

        _jobs[job.Id] = job;

        var qlock = GetQueueLock(job.QueueKey);
        lock (qlock)
        {
            IndexIntoQueue_NoLock(job);
        }

        _logger.LogDebug(
            "Inserted job {JobId} into queue {QueueKey} with ScheduledAt={ScheduledAt:o}",
            job.Id,
            job.QueueKey,
            job.ScheduledAt
        );

        return Task.FromResult(job.Id);
    }

    public Task UpdateAsync(AtomizerJob job, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_jobs.TryGetValue(job.Id, out var existing))
        {
            _logger.LogDebug("Update requested for missing job {JobId}", job.Id);
            throw new KeyNotFoundException($"Job {job.Id} not found");
        }

        if (existing.QueueKey != job.QueueKey)
        {
            // Remove from old queue
            var oldLock = GetQueueLock(existing.QueueKey);
            lock (oldLock)
            {
                UnindexFromQueue_NoLock(existing);
            }

            // Add to new queue
            var newLock = GetQueueLock(job.QueueKey);
            lock (newLock)
            {
                IndexIntoQueue_NoLock(job);
            }

            _logger.LogDebug(
                "Job {JobId} re-indexed from queue {OldQueueKey} to {NewQueueKey}",
                job.Id,
                existing.QueueKey,
                job.QueueKey
            );
        }

        _jobs[job.Id] = job;

        _logger.LogDebug("Updated job {JobId} status={Status} attempts={Attempts}", job.Id, job.Status, job.Attempts);

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

        _logger.LogDebug(
            "LeaseBatch requested queue={QueueKey} batchSize={BatchSize} now={Now:o} leaseToken={LeaseToken}",
            queueKey,
            batchSize,
            now,
            leaseToken.Token
        );

        var qlock = GetQueueLock(queueKey);
        List<AtomizerJob> candidates;

        lock (qlock)
        {
            if (!_queues.TryGetValue(queueKey, out var ids) || ids.Count == 0)
            {
                _logger.LogDebug("LeaseBatch: queue {QueueKey} is empty", queueKey);
                return Task.FromResult((IReadOnlyList<AtomizerJob>)Array.Empty<AtomizerJob>());
            }

            candidates = ids.Select(id => _jobs[id]) // safe: ids derived under the same lock
                .Where(j =>
                    (
                        j.Status == AtomizerJobStatus.Pending
                        && (j.VisibleAt == null || j.VisibleAt <= now)
                        && j.ScheduledAt <= now
                    ) || (j.Status == AtomizerJobStatus.Processing && j.VisibleAt <= now) // expired lease
                )
                .OrderBy(j => j.ScheduledAt)
                .ThenBy(j => j.CreatedAt)
                .Take(Math.Max(0, batchSize))
                .ToList();

            if (candidates.Count == 0)
            {
                _logger.LogDebug("LeaseBatch: no eligible candidates for queue {QueueKey}", queueKey);
                return Task.FromResult((IReadOnlyList<AtomizerJob>)Array.Empty<AtomizerJob>());
            }

            // Transition under queue lock to keep queue state consistent
            foreach (var job in candidates)
            {
                job.Lease(leaseToken, now, visibilityTimeout);

                var set = _leasesByToken.GetOrAdd(leaseToken.Token, _ => new ConcurrentDictionary<Guid, byte>());
                set[job.Id] = 0;
            }
        }

        _logger.LogDebug(
            "LeaseBatch: leased {Count} jobs from queue {QueueKey}: [{Ids}]",
            candidates.Count,
            queueKey,
            string.Join(",", candidates.Select(c => c.Id))
        );

        return Task.FromResult((IReadOnlyList<AtomizerJob>)candidates);
    }

    public Task<int> ReleaseLeasedAsync(LeaseToken leaseToken, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = _clock.UtcNow;

        if (!_leasesByToken.TryRemove(leaseToken.Token, out var leasedIds) || leasedIds.Count == 0)
        {
            _logger.LogDebug("ReleaseLeased: no jobs found for leaseToken={LeaseToken}", leaseToken.Token);
            return Task.FromResult(0);
        }

        var released = 0;

        foreach (var jobId in leasedIds.Keys.ToList())
        {
            if (!_jobs.TryGetValue(jobId, out var job))
                continue;

            var qlock = GetQueueLock(job.QueueKey);
            lock (qlock)
            {
                // Double-check token under lock in case it changed
                if (job.LeaseToken?.Token != leaseToken.Token)
                    continue;

                job.Release(now);
                released++;
            }
        }

        _logger.LogDebug(
            "ReleaseLeased: released {Count} jobs for leaseToken={LeaseToken}",
            released,
            leaseToken.Token
        );
        return Task.FromResult(released);
    }

    public Task<Guid> UpsertScheduleAsync(AtomizerSchedule schedule, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = _clock.UtcNow;

        lock (_schedulesSync)
        {
            schedule.CreatedAt = schedule.CreatedAt == default ? now : schedule.CreatedAt;
            schedule.UpdatedAt = now;
            _schedules[schedule.JobKey] = schedule;

            _logger.LogDebug("UpsertSchedule: upserted schedule for jobKey={JobKey}", schedule.JobKey);
            return Task.FromResult(schedule.Id);
        }
    }

    public Task<IReadOnlyList<AtomizerSchedule>> LeaseDueSchedulesAsync(
        DateTimeOffset now,
        TimeSpan visibilityTimeout,
        LeaseToken leaseToken,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogDebug("LeaseDueSchedules requested now={Now:o} leaseToken={LeaseToken}", now, leaseToken.Token);

        List<AtomizerSchedule> due;
        lock (_schedulesSync)
        {
            due = _schedules
                .Values.Where(s => s.Enabled && s.NextRunAt <= now && (s.VisibleAt == null || s.VisibleAt <= now))
                .OrderBy(s => s.NextRunAt)
                .ThenBy(s => s.CreatedAt)
                .ToList();

            if (due.Count == 0)
            {
                _logger.LogDebug("LeaseDueSchedules: no due schedules");
                return Task.FromResult((IReadOnlyList<AtomizerSchedule>)Array.Empty<AtomizerSchedule>());
            }

            foreach (var s in due)
            {
                s.Lease(now, visibilityTimeout, leaseToken);
            }
        }

        _logger.LogDebug(
            "LeaseDueSchedules: leased {Count} schedules: [{Keys}]",
            due.Count,
            string.Join(",", due.Select(x => x.JobKey))
        );

        return Task.FromResult((IReadOnlyList<AtomizerSchedule>)due);
    }

    public Task<int> ReleaseLeasedSchedulesAsync(LeaseToken leaseToken, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = _clock.UtcNow;

        int affected = 0;
        lock (_schedulesSync)
        {
            foreach (var s in _schedules.Values)
            {
                if (s.LeaseToken?.Token == leaseToken.Token)
                {
                    s.Release(now);
                    affected++;
                }
            }
        }

        _logger.LogDebug(
            "ReleaseLeasedSchedules: released {Count} schedule(s) for leaseToken={LeaseToken}",
            affected,
            leaseToken.Token
        );

        return Task.FromResult(affected);
    }

    // ---- helpers ----

    private object GetQueueLock(QueueKey key) => _queueLocks.GetOrAdd(key, _ => new object());

    private void IndexIntoQueue_NoLock(AtomizerJob job)
    {
        if (!_queues.TryGetValue(job.QueueKey, out var ids))
        {
            ids = new HashSet<Guid>();
            _queues[job.QueueKey] = ids;
        }
        ids.Add(job.Id);
    }

    private void UnindexFromQueue_NoLock(AtomizerJob job)
    {
        if (_queues.TryGetValue(job.QueueKey, out var ids))
        {
            ids.Remove(job.Id);
            if (ids.Count == 0)
            {
                _queues.Remove(job.QueueKey);
            }
        }
    }

    private void Evict()
    {
        var retain = Math.Max(0, _options.AmountOfJobsToRetainInMemory);

        // Snapshot enumeration is safe on ConcurrentDictionary
        var terminal = _jobs
            .Values.Where(j => j.Status == AtomizerJobStatus.Completed || j.Status == AtomizerJobStatus.Failed)
            .OrderByDescending(j => j.UpdatedAt)
            .ToList();

        var toRemove = terminal.Skip(retain).ToList();
        if (toRemove.Count == 0)
            return;

        var removed = 0;

        foreach (var job in toRemove)
        {
            // Remove from queue index under that queue's lock
            var qlock = GetQueueLock(job.QueueKey);
            lock (qlock)
            {
                UnindexFromQueue_NoLock(job);
            }

            // Remove from leases set (if any)
            var leaseToken = job.LeaseToken?.Token;
            if (!string.IsNullOrEmpty(leaseToken) && _leasesByToken.TryGetValue(leaseToken!, out var set))
            {
                set.TryRemove(job.Id, out _);
                if (set.IsEmpty)
                {
                    _leasesByToken.TryRemove(leaseToken!, out _);
                }
            }

            // Finally remove from jobs
            _jobs.TryRemove(job.Id, out _);
            removed++;
        }

        if (removed > 0)
        {
            _logger.LogDebug(
                "Evicted {Count} completed/failed jobs; retaining {Retain} most-recent terminal jobs",
                removed,
                retain
            );
        }
    }
}
