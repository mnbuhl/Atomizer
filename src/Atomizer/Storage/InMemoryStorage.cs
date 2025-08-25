using Atomizer.Abstractions;
using Atomizer.Core;
using Microsoft.Extensions.Logging;

namespace Atomizer.Storage;

public sealed class InMemoryStorage : IAtomizerStorage
{
    private readonly object _sync = new object();
    private readonly Dictionary<Guid, AtomizerJob> _jobs = new Dictionary<Guid, AtomizerJob>();
    private readonly Dictionary<QueueKey, HashSet<Guid>> _queues = new Dictionary<QueueKey, HashSet<Guid>>();
    private readonly Dictionary<string, HashSet<Guid>> _leasesByToken = new Dictionary<string, HashSet<Guid>>();

    private readonly Dictionary<JobKey, AtomizerSchedule> _schedules = new Dictionary<JobKey, AtomizerSchedule>();

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

        lock (_sync)
        {
            Evict();
            if (job.Id == Guid.Empty)
            {
                job.Id = Guid.NewGuid();
            }

            _jobs[job.Id] = job;
            IndexIntoQueue(job);

            _logger.LogDebug(
                "Inserted job {JobId} into queue {QueueKey} with ScheduledAt={ScheduledAt:o}",
                job.Id,
                job.QueueKey,
                job.ScheduledAt
            );

            return Task.FromResult(job.Id);
        }
    }

    public Task UpdateAsync(AtomizerJob job, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_sync)
        {
            Evict();

            if (!_jobs.TryGetValue(job.Id, out var existing))
            {
                // Upsert semantics for jobs aren’t specified; choose strict update
                // to surface mistakes early.
                _logger.LogDebug("Update requested for missing job {JobId}", job.Id);
                throw new KeyNotFoundException($"Job {job.Id} not found");
            }

            // If queue changed, reindex
            if (existing.QueueKey != job.QueueKey)
            {
                UnindexFromQueue(existing);
                IndexIntoQueue(job);

                _logger.LogDebug(
                    "Job {JobId} re-indexed from queue {OldQueueKey} to {NewQueueKey}",
                    job.Id,
                    existing.QueueKey,
                    job.QueueKey
                );
            }

            _jobs[job.Id] = job;

            _logger.LogDebug(
                "Updated job {JobId} status={Status} attempts={Attempts}",
                job.Id,
                job.Status,
                job.Attempts
            );

            return Task.CompletedTask;
        }
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

        lock (_sync)
        {
            _logger.LogDebug(
                "LeaseBatch requested queue={QueueKey} batchSize={BatchSize} now={Now:o} leaseToken={LeaseToken}",
                queueKey,
                batchSize,
                now,
                leaseToken.Token
            );

            if (!_queues.TryGetValue(queueKey, out var ids) || ids.Count == 0)
            {
                return Task.FromResult((IReadOnlyList<AtomizerJob>)Array.Empty<AtomizerJob>());
            }

            var candidates = ids.Select(id => _jobs[id])
                .Where(j =>
                    j.Status == AtomizerJobStatus.Pending
                        && (j.VisibleAt == null || j.VisibleAt <= now)
                        && j.ScheduledAt <= now
                    || (j.Status == AtomizerJobStatus.Processing && j.VisibleAt <= now) // lease expired
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

            foreach (var job in candidates)
            {
                job.Lease(leaseToken, now, visibilityTimeout);

                if (!_leasesByToken.TryGetValue(leaseToken.Token, out var leased))
                {
                    leased = new HashSet<Guid>();
                    _leasesByToken[leaseToken.Token] = leased;
                }
                leased.Add(job.Id);
            }

            _logger.LogDebug(
                "LeaseBatch: leased {Count} jobs from queue {QueueKey}: [{Ids}]",
                candidates.Count,
                queueKey,
                string.Join(",", candidates.Select(c => c.Id))
            );

            return Task.FromResult((IReadOnlyList<AtomizerJob>)candidates);
        }
    }

    public Task<int> ReleaseLeasedAsync(LeaseToken leaseToken, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = DateTimeOffset.UtcNow;

        lock (_sync)
        {
            Evict();
            if (!_leasesByToken.TryGetValue(leaseToken.Token, out var leasedIds) || leasedIds.Count == 0)
            {
                return Task.FromResult(0);
            }

            var releasedCount = 0;

            foreach (var jobId in leasedIds.ToList())
            {
                if (!_jobs.TryGetValue(jobId, out var job))
                    continue;

                if (job.LeaseToken?.Token != leaseToken.Token)
                    continue;

                job.Release(now);
                releasedCount++;
            }

            _leasesByToken.Remove(leaseToken.Token);

            _logger.LogDebug(
                "ReleaseLeased: released {Count} jobs for leaseToken={LeaseToken}",
                releasedCount,
                leaseToken.Token
            );

            return Task.FromResult(releasedCount);
        }
    }

    public Task<Guid> UpsertScheduleAsync(AtomizerSchedule schedule, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = _clock.UtcNow;

        lock (_sync)
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

        lock (_sync)
        {
            _logger.LogDebug("LeaseDueSchedules requested now={Now:o} leaseToken={LeaseToken}", now, leaseToken.Token);

            // One scheduler poller => no need for per-queue partition here.
            var due = _schedules
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

            _logger.LogDebug(
                "LeaseDueSchedules: leased {Count} schedules: [{Keys}]",
                due.Count,
                string.Join(",", due.Select(x => x.JobKey))
            );

            return Task.FromResult((IReadOnlyList<AtomizerSchedule>)due);
        }
    }

    public Task<int> ReleaseLeasedSchedulesAsync(LeaseToken leaseToken, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = _clock.UtcNow;

        lock (_sync)
        {
            var affected = 0;
            foreach (var s in _schedules.Values)
            {
                if (s.LeaseToken?.Token == leaseToken.Token)
                {
                    s.Release(now);
                    affected++;
                }
            }

            _logger.LogDebug(
                "ReleaseLeasedSchedules: released {Count} schedule(s) for leaseToken={LeaseToken}",
                affected,
                leaseToken.Token
            );

            return Task.FromResult(affected);
        }
    }

    // ---- helpers ----

    private void IndexIntoQueue(AtomizerJob job)
    {
        if (!_queues.TryGetValue(job.QueueKey, out var ids))
        {
            ids = new HashSet<Guid>();
            _queues[job.QueueKey] = ids;
        }
        ids.Add(job.Id);
    }

    private void UnindexFromQueue(AtomizerJob job)
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
        var amountOfCompletedJobsToRetain = Math.Max(0, _options.AmountOfJobsToRetainInMemory);

        // Evict terminal jobs older than retention
        var toRemove = _jobs
            .Values.Where(j => j.Status == AtomizerJobStatus.Completed || j.Status == AtomizerJobStatus.Failed)
            .OrderByDescending(j => j.UpdatedAt)
            .Skip(amountOfCompletedJobsToRetain)
            .Select(j => j.Id)
            .ToList();

        if (toRemove.Count == 0)
            return;

        var removed = 0;

        foreach (var id in toRemove)
        {
            if (_jobs.TryGetValue(id, out var job))
            {
                UnindexFromQueue(job);
                if (job.LeaseToken != null && _leasesByToken.TryGetValue(job.LeaseToken.Token, out var set))
                {
                    set.Remove(id);
                    if (set.Count == 0)
                        _leasesByToken.Remove(job.LeaseToken.Token);
                }
            }
            _jobs.Remove(id);
            removed++;
        }

        if (removed > 0)
        {
            _logger.LogDebug(
                "Evicted {Count} completed/failed jobs; retaining {Retain} most-recent terminal jobs",
                removed,
                amountOfCompletedJobsToRetain
            );
        }
    }
}
