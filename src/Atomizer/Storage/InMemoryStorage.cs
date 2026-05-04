using System.Collections.Concurrent;
using Atomizer.Abstractions;
using Atomizer.Core;
using Microsoft.Extensions.Logging;

namespace Atomizer.Storage;

/// <summary>
/// In-process implementation of <see cref="IAtomizerStorage"/> backed by concurrent dictionaries.
/// </summary>
public sealed class InMemoryStorage : IAtomizerStorage
{
    private readonly ConcurrentDictionary<Guid, AtomizerJob> _jobs = new();
    private readonly ConcurrentDictionary<QueueKey, ConcurrentDictionary<Guid, byte>> _queues = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, byte>> _leasesByToken = new();
    private readonly ConcurrentDictionary<string, AtomizerActiveServer> _activeServers = new(StringComparer.Ordinal);
    private readonly object _syncRoot = new();

    private readonly Dictionary<JobKey, AtomizerSchedule> _schedules = new();
    private readonly ConcurrentDictionary<QueueKey, SemaphoreSlim> _semaphores = new();

    private readonly InMemoryJobStorageOptions _options;
    private readonly IAtomizerClock _clock;
    private readonly ILogger<InMemoryStorage> _logger;

    /// <summary>
    /// Initializes a new <see cref="InMemoryStorage"/> with the specified options, clock, and logger.
    /// </summary>
    /// <param name="options">Options controlling storage behaviour such as job retention limits.</param>
    /// <param name="clock">Clock abstraction for obtaining the current UTC time.</param>
    /// <param name="logger">Logger for diagnostic output.</param>
    public InMemoryStorage(InMemoryJobStorageOptions options, IAtomizerClock clock, ILogger<InMemoryStorage> logger)
    {
        _options = options;
        _clock = clock;
        _logger = logger;
    }

    /// <inheritdoc/>
    public Task<Guid> InsertAsync(AtomizerJob job, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _jobs[job.Id] = job;

        IndexIntoQueue(job);

        _logger.LogDebug(
            "Inserted job {JobId} into queue {QueueKey} with ScheduledAt={ScheduledAt:o}",
            job.Id,
            job.QueueKey,
            job.ScheduledAt
        );

        EvictCompletedAndFailed();

        return Task.FromResult(job.Id);
    }

    /// <inheritdoc/>
    public Task UpdateJobsAsync(IEnumerable<AtomizerJob> jobs, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var jobsList = jobs.ToList();

        foreach (var job in jobsList)
        {
            if (!_jobs.TryGetValue(job.Id, out _))
            {
                throw new InvalidOperationException(
                    $"Update requested for job {job.Id} that no longer exists in storage."
                );
            }

            _jobs[job.Id] = job;

            UpdateLease(job);
        }

        _logger.LogDebug("Updated {Count} jobs", jobsList.Count);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
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

        List<AtomizerJob> candidates;

        if (!_queues.TryGetValue(queueKey, out var ids) || ids.IsEmpty)
        {
            _logger.LogDebug("LeaseBatch: queue {QueueKey} is empty", queueKey);
            return Task.FromResult((IReadOnlyList<AtomizerJob>)Array.Empty<AtomizerJob>());
        }

        candidates = ids
            .Keys.Select(id => _jobs.TryGetValue(id, out var j) ? j : null)
            .Where(j =>
                j != null
                && (
                    (
                        j.Status == AtomizerJobStatus.Pending
                        && (j.VisibleAt == null || j.VisibleAt <= now)
                        && j.ScheduledAt <= now
                    ) || (j.Status == AtomizerJobStatus.Processing && j.VisibleAt <= now) // expired lease
                )
            )
            .Select(j => j!)
            .OrderBy(j => j.ScheduledAt)
            .ThenBy(j => j.CreatedAt)
            .Take(Math.Max(0, batchSize))
            .ToList();

        if (candidates.Count == 0)
        {
            _logger.LogDebug("LeaseBatch: no eligible candidates for queue {QueueKey}", queueKey);
            return Task.FromResult((IReadOnlyList<AtomizerJob>)Array.Empty<AtomizerJob>());
        }

        _logger.LogDebug(
            "LeaseBatch: leased {Count} jobs from queue {QueueKey}: [{Ids}]",
            candidates.Count,
            queueKey,
            string.Join(",", candidates.Select(c => c.Id))
        );

        return Task.FromResult((IReadOnlyList<AtomizerJob>)candidates);
    }

    /// <inheritdoc/>
    public Task<int> ReleaseLeasedAsync(LeaseToken leaseToken, DateTimeOffset now, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

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

            // Double-check token under lock in case it changed
            if (job.LeaseToken?.Token != leaseToken.Token)
                continue;

            job.Release(now);
            released++;
        }

        _logger.LogDebug(
            "ReleaseLeased: released {Count} jobs for leaseToken={LeaseToken}",
            released,
            leaseToken.Token
        );
        return Task.FromResult(released);
    }

    /// <inheritdoc/>
    public Task UpsertHeartbeatAsync(AtomizerActiveServer server, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(server.InstanceId))
        {
            throw new ArgumentException("Active server instance id cannot be null or empty.", nameof(server));
        }

        lock (_syncRoot)
        {
            _activeServers[server.InstanceId] = new AtomizerActiveServer
            {
                InstanceId = server.InstanceId,
                LastHeartbeatAt = server.LastHeartbeatAt,
            };
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<AtomizerActiveServer>> GetStaleServersAsync(
        DateTimeOffset staleBefore,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<AtomizerActiveServer> staleServers;
        lock (_syncRoot)
        {
            staleServers = _activeServers
                .Values.Where(server => server.LastHeartbeatAt < staleBefore)
                .Select(server => new AtomizerActiveServer
                {
                    InstanceId = server.InstanceId,
                    LastHeartbeatAt = server.LastHeartbeatAt,
                })
                .OrderBy(server => server.LastHeartbeatAt)
                .ToList();
        }

        return Task.FromResult(staleServers);
    }

    /// <inheritdoc/>
    public Task<AtomizerHeartbeatRecoveryResult> TryRecoverStaleServerAsync(
        string instanceId,
        DateTimeOffset staleBefore,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            if (
                !_activeServers.TryGetValue(instanceId, out var server)
                || server.LastHeartbeatAt >= staleBefore
                || !_activeServers.TryRemove(instanceId, out _)
            )
            {
                return Task.FromResult(AtomizerHeartbeatRecoveryResult.NotRecovered(instanceId));
            }

            var prefix = instanceId + LeaseToken.Delimiter;
            var released = ReleaseMatchingJobs(
                job =>
                    job.Status == AtomizerJobStatus.Processing
                    && job.LeaseToken?.Token.StartsWith(prefix, StringComparison.Ordinal) == true,
                now
            );

            return Task.FromResult(new AtomizerHeartbeatRecoveryResult(instanceId, true, released));
        }
    }

    /// <inheritdoc/>
    public Task RemoveHeartbeatAsync(string instanceId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _activeServers.TryRemove(instanceId, out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public async Task<Guid> UpsertScheduleAsync(AtomizerSchedule schedule, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var scheduleLock = _semaphores.GetOrAdd(QueueKey.Scheduler, _ => new SemaphoreSlim(1, 1));
        await scheduleLock.WaitAsync(cancellationToken);
        try
        {
            var now = _clock.UtcNow;
            schedule.CreatedAt = schedule.CreatedAt == default ? now : schedule.CreatedAt;
            schedule.UpdatedAt = now;
            _schedules[schedule.JobKey] = schedule;
            _logger.LogDebug("UpsertSchedule: upserted schedule for jobKey={JobKey}", schedule.JobKey);
            return schedule.Id;
        }
        finally
        {
            scheduleLock.Release();
        }
    }

    /// <inheritdoc/>
    public Task UpdateSchedulesAsync(IEnumerable<AtomizerSchedule> schedules, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var now = _clock.UtcNow;
        var schedulesList = schedules.ToList();

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

        _logger.LogDebug("UpdateSchedules: updated {Count} schedules", schedulesList.Count);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<AtomizerSchedule>> GetDueSchedulesAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        _logger.LogDebug("GetDueSchedules requested now={Now:o}", now);

        var due = _schedules
            .Values.Where(s => s.Enabled && s.NextRunAt <= now)
            .OrderBy(s => s.NextRunAt)
            .ThenBy(s => s.CreatedAt)
            .ToList();

        if (due.Count == 0)
        {
            _logger.LogDebug("LeaseDueSchedules: no due schedules");
            return Task.FromResult((IReadOnlyList<AtomizerSchedule>)Array.Empty<AtomizerSchedule>());
        }

        _logger.LogDebug(
            "LeaseDueSchedules: leased {Count} schedules: [{Keys}]",
            due.Count,
            string.Join(",", due.Select(x => x.JobKey))
        );

        return Task.FromResult((IReadOnlyList<AtomizerSchedule>)due);
    }

    /// <inheritdoc/>
    public async Task<TResult> ExecuteInLeaseAsync<TResult>(
        QueueKey queue,
        Func<CancellationToken, Task<TResult>> callback,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var semaphore = _semaphores.GetOrAdd(queue, _ => new SemaphoreSlim(1, 1));
        var acquired = await semaphore.WaitAsync(TimeSpan.Zero, cancellationToken);

        if (!acquired)
        {
            _logger.LogDebug("ExecuteInLeaseAsync: skipping tick for queue '{QueueKey}' — lease already held", queue);
            return default!;
        }

        try
        {
            return await callback(cancellationToken);
        }
        finally
        {
            semaphore.Release();
        }
    }

    /// <inheritdoc/>
    public Task ExecuteInLeaseAsync(
        QueueKey queue,
        Func<CancellationToken, Task> callback,
        CancellationToken cancellationToken
    ) =>
        ExecuteInLeaseAsync<bool>(
            queue,
            async ct =>
            {
                await callback(ct);
                return true;
            },
            cancellationToken
        );

    // ---- helpers ----

    private void IndexIntoQueue(AtomizerJob job)
    {
        var ids = _queues.GetOrAdd(job.QueueKey, _ => new ConcurrentDictionary<Guid, byte>());
        ids[job.Id] = 0;
    }

    private void UnindexFromQueue(AtomizerJob job)
    {
        if (_queues.TryGetValue(job.QueueKey, out var ids))
        {
            ids.TryRemove(job.Id, out _);
            // Do NOT remove empty outer key — TOCTOU race with concurrent IndexIntoQueue
        }
    }

    private void EvictCompletedAndFailed()
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
            UnindexFromQueue(job);

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


    private int ReleaseMatchingJobs(Func<AtomizerJob, bool> predicate, DateTimeOffset now)
    {
        var released = 0;

        foreach (var job in _jobs.Values.ToList())
        {
            var leaseToken = job.LeaseToken?.Token;
            if (!predicate(job))
                continue;

            job.Release(now);
            released++;

            if (leaseToken != null && _leasesByToken.TryGetValue(leaseToken, out var leaseSet))
            {
                leaseSet.TryRemove(job.Id, out _);
                if (leaseSet.IsEmpty)
                {
                    _leasesByToken.TryRemove(leaseToken, out _);
                }
            }
        }

        return released;
    }

    private void UpdateLease(AtomizerJob job)
    {
        if (job.LeaseToken != null)
        {
            var leaseSet = _leasesByToken.GetOrAdd(job.LeaseToken.Token, _ => new ConcurrentDictionary<Guid, byte>());
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
}
