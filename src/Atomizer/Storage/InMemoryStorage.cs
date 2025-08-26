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
            IndexIntoQueueWithoutLock(job);
        }

        _logger.LogDebug(
            "Inserted job {JobId} into queue {QueueKey} with ScheduledAt={ScheduledAt:o}",
            job.Id,
            job.QueueKey,
            job.ScheduledAt
        );

        return Task.FromResult(job.Id);
    }

    public Task UpdateJobAsync(AtomizerJob job, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!_jobs.TryGetValue(job.Id, out _))
        {
            _logger.LogDebug("Update requested for missing job {JobId}", job.Id);
            throw new KeyNotFoundException($"Job {job.Id} not found");
        }

        _jobs[job.Id] = job;

        _logger.LogDebug("Updated job {JobId} status={Status} attempts={Attempts}", job.Id, job.Status, job.Attempts);

        return Task.CompletedTask;
    }

    public Task UpdateJobsAsync(IEnumerable<AtomizerJob> jobs, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var jobsList = jobs.ToList();

        foreach (var job in jobsList)
        {
            if (!_jobs.TryGetValue(job.Id, out _))
            {
                _logger.LogDebug("Update requested for missing job {JobId}", job.Id);
                continue;
            }

            _jobs[job.Id] = job;

            if (job.LeaseToken != null)
            {
                var leaseSet = _leasesByToken.GetOrAdd(
                    job.LeaseToken.Token,
                    _ => new ConcurrentDictionary<Guid, byte>()
                );
                leaseSet[job.Id] = 0;
            }
            else
            {
                // Remove from any existing lease set
                foreach (var lease in _leasesByToken.Values)
                {
                    lease.TryRemove(job.Id, out _);
                }
            }
        }

        _logger.LogDebug("Updated {Count} jobs", jobsList.Count);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AtomizerJob>> GetDueJobsAsync(
        QueueKey queueKey,
        DateTimeOffset now,
        int batchSize,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogDebug(
            "LeaseBatch requested queue={QueueKey} batchSize={BatchSize} now={Now:o}",
            queueKey,
            batchSize,
            now
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

    public Task UpdateSchedulesAsync(IEnumerable<AtomizerSchedule> schedules, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = _clock.UtcNow;

        var schedulesList = schedules.ToList();

        lock (_schedulesSync)
        {
            foreach (var schedule in schedulesList)
            {
                if (!_schedules.ContainsKey(schedule.JobKey))
                {
                    _logger.LogDebug("UpdateSchedules: schedule for jobKey={JobKey} not found", schedule.JobKey);
                    continue;
                }

                schedule.UpdatedAt = now;
                _schedules[schedule.JobKey] = schedule;
            }
        }

        _logger.LogDebug("UpdateSchedules: updated {Count} schedules", schedulesList.Count);
        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AtomizerSchedule>> GetDueSchedulesAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogDebug("GetDueSchedules requested now={Now:o}", now);

        List<AtomizerSchedule> due;
        lock (_schedulesSync)
        {
            due = _schedules
                .Values.Where(s => s.Enabled && s.NextRunAt <= now)
                .OrderBy(s => s.NextRunAt)
                .ThenBy(s => s.CreatedAt)
                .ToList();

            if (due.Count == 0)
            {
                _logger.LogDebug("LeaseDueSchedules: no due schedules");
                return Task.FromResult((IReadOnlyList<AtomizerSchedule>)Array.Empty<AtomizerSchedule>());
            }
        }

        _logger.LogDebug(
            "LeaseDueSchedules: leased {Count} schedules: [{Keys}]",
            due.Count,
            string.Join(",", due.Select(x => x.JobKey))
        );

        return Task.FromResult((IReadOnlyList<AtomizerSchedule>)due);
    }

    public Task<IAtomizerLock> AcquireLockAsync(
        QueueKey queueKey,
        TimeSpan lockTimeout,
        CancellationToken cancellationToken
    )
    {
        return Task.FromResult<IAtomizerLock>(new NoopLock());
    }

    // ---- helpers ----

    private object GetQueueLock(QueueKey key) => _queueLocks.GetOrAdd(key, _ => new object());

    private void IndexIntoQueueWithoutLock(AtomizerJob job)
    {
        if (!_queues.TryGetValue(job.QueueKey, out var ids))
        {
            ids = new HashSet<Guid>();
            _queues[job.QueueKey] = ids;
        }
        ids.Add(job.Id);
    }

    private void UnindexFromQueueWithoutLock(AtomizerJob job)
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
                UnindexFromQueueWithoutLock(job);
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
