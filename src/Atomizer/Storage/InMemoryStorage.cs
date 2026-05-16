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
    private readonly ConcurrentDictionary<QueueKey, ConcurrentDictionary<string, long>> _partitionSequences = new();
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

        if (job.IdempotencyKey != null)
        {
            var existing = _jobs.Values.FirstOrDefault(j => j.IdempotencyKey == job.IdempotencyKey);
            if (existing != null)
            {
                job.SequenceNumber = existing.SequenceNumber;
                return Task.FromResult(existing.Id);
            }
        }

        if (job.PartitionKey != null)
        {
            var partitionSequences = _partitionSequences.GetOrAdd(
                job.QueueKey,
                _ => new ConcurrentDictionary<string, long>()
            );
            var seq = partitionSequences.AddOrUpdate(job.PartitionKey.Key, 1L, (_, current) => current + 1L);
            job.SequenceNumber = seq;
        }

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

        if (!_queues.TryGetValue(queueKey, out var ids) || ids.IsEmpty)
        {
            _logger.LogDebug("LeaseBatch: queue {QueueKey} is empty", queueKey);
            return Task.FromResult((IReadOnlyList<AtomizerJob>)Array.Empty<AtomizerJob>());
        }

        var blockedPartitions = new HashSet<string>();
        foreach (var id in ids.Keys)
        {
            if (_jobs.TryGetValue(id, out var bj) && bj.IsPartitionBlocked)
                blockedPartitions.Add(bj.PartitionKey!.Key);
        }

        var eligible = ids
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
                && (j.PartitionKey == null || !blockedPartitions.Contains(j.PartitionKey.Key))
            )
            .Select(j => j!);

        var unpartitionedCandidates = eligible
            .Where(j => j.PartitionKey == null)
            .Select(j => new DueJobCandidate(j, j.ScheduledAt, j.CreatedAt, string.Empty, long.MaxValue));

        var partitionedCandidates = eligible
            .Where(j => j.PartitionKey != null)
            .GroupBy(j => j.PartitionKey!.Key)
            .SelectMany(group =>
            {
                var orderedJobs = group
                    .OrderBy(j => j.SequenceNumber ?? long.MaxValue)
                    .ThenBy(j => j.ScheduledAt)
                    .ThenBy(j => j.CreatedAt)
                    .ThenBy(j => j.Id)
                    .ToList();
                var head = orderedJobs[0];

                return orderedJobs.Select(j => new DueJobCandidate(
                    j,
                    head.ScheduledAt,
                    head.CreatedAt,
                    group.Key,
                    j.SequenceNumber ?? long.MaxValue
                ));
            });

        var candidates = unpartitionedCandidates
            .Concat(partitionedCandidates)
            .OrderBy(candidate => candidate.SortScheduledAt)
            .ThenBy(candidate => candidate.SortCreatedAt)
            .ThenBy(candidate => candidate.SortPartitionKey, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.SortSequenceNumber)
            .ThenBy(candidate => candidate.Job.ScheduledAt)
            .ThenBy(candidate => candidate.Job.CreatedAt)
            .ThenBy(candidate => candidate.Job.Id)
            .Select(candidate => candidate.Job)
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

    private sealed class DueJobCandidate
    {
        public DueJobCandidate(
            AtomizerJob job,
            DateTimeOffset sortScheduledAt,
            DateTimeOffset sortCreatedAt,
            string sortPartitionKey,
            long sortSequenceNumber
        )
        {
            Job = job;
            SortScheduledAt = sortScheduledAt;
            SortCreatedAt = sortCreatedAt;
            SortPartitionKey = sortPartitionKey;
            SortSequenceNumber = sortSequenceNumber;
        }

        public AtomizerJob Job { get; }

        public DateTimeOffset SortScheduledAt { get; }

        public DateTimeOffset SortCreatedAt { get; }

        public string SortPartitionKey { get; }

        public long SortSequenceNumber { get; }
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
            if (_schedules.TryGetValue(schedule.JobKey, out var existing))
            {
                schedule.Id = existing.Id;
                schedule.CreatedAt = existing.CreatedAt;
                schedule.Enabled = existing.Enabled;
                schedule.NextRunAt = existing.NextRunAt;
                schedule.LastEnqueueAt = existing.LastEnqueueAt;
            }
            else
            {
                schedule.CreatedAt = schedule.CreatedAt == default ? now : schedule.CreatedAt;
            }

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
    public async Task<bool> DeleteScheduleAsync(JobKey jobKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var scheduleLock = _semaphores.GetOrAdd(QueueKey.Scheduler, _ => new SemaphoreSlim(1, 1));
        await scheduleLock.WaitAsync(cancellationToken);
        try
        {
            var removed = _schedules.Remove(jobKey);
            if (removed)
            {
                _logger.LogDebug("DeleteSchedule: deleted schedule for jobKey={JobKey}", jobKey);
            }

            return removed;
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
    public Task<PagedResult<AtomizerJob>> GetJobsAsync(JobQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var take = Math.Min(query.Take, 500);

        var jobs = ApplyJobQueryFilters(_jobs.Values, query, includeStatusFilter: true);

        var ordered = jobs.OrderByDescending(j => j.CreatedAt).ToList();
        var total = ordered.Count;
        var items = ordered.Skip(query.Skip).Take(take).ToList();

        return Task.FromResult(
            new PagedResult<AtomizerJob>
            {
                Items = items,
                TotalCount = total,
                Skip = query.Skip,
                Take = take,
            }
        );
    }

    /// <inheritdoc/>
    public Task<JobStatusCounts> GetJobStatusCountsAsync(JobQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var jobs = ApplyJobQueryFilters(_jobs.Values, query, includeStatusFilter: false).ToList();

        return Task.FromResult(
            new JobStatusCounts
            {
                Pending = jobs.Count(j => j.Status == AtomizerJobStatus.Pending),
                Processing = jobs.Count(j => j.Status == AtomizerJobStatus.Processing),
                Completed = jobs.Count(j => j.Status == AtomizerJobStatus.Completed),
                Failed = jobs.Count(j => j.Status == AtomizerJobStatus.Failed),
                Cancelled = jobs.Count(j => j.Status == AtomizerJobStatus.Cancelled),
            }
        );
    }

    private static IEnumerable<AtomizerJob> ApplyJobQueryFilters(
        IEnumerable<AtomizerJob> jobs,
        JobQuery query,
        bool includeStatusFilter
    )
    {
        if (includeStatusFilter && query.Statuses is { Count: > 0 })
            jobs = jobs.Where(j => query.Statuses.Contains(j.Status));

        if (query.QueueKey is not null)
            jobs = jobs.Where(j => j.QueueKey == query.QueueKey);

        if (query.PayloadTypeName is not null)
            jobs = jobs.Where(j =>
                j.PayloadType != null
                && j.PayloadType.Name.IndexOf(query.PayloadTypeName, StringComparison.OrdinalIgnoreCase) >= 0
            );

        if (query.CreatedFromUtc.HasValue)
            jobs = jobs.Where(j => j.CreatedAt >= query.CreatedFromUtc.Value);

        if (query.CreatedToUtc.HasValue)
            jobs = jobs.Where(j => j.CreatedAt <= query.CreatedToUtc.Value);

        return jobs;
    }

    /// <inheritdoc/>
    public Task<AtomizerJob?> GetJobByIdAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        _jobs.TryGetValue(id, out var job);
        return Task.FromResult<AtomizerJob?>(job);
    }

    /// <inheritdoc/>
    public Task<int> DeleteExpiredJobsAsync(DateTimeOffset terminalBefore, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var expired = _jobs
            .Values.Where(job =>
            {
                var terminalAt = GetTerminalTimestamp(job);
                return terminalAt.HasValue && terminalAt.Value < terminalBefore;
            })
            .ToList();

        var removed = RemoveJobs(expired);
        if (removed > 0)
        {
            _logger.LogDebug("Deleted {Count} terminal jobs older than {TerminalBefore:o}", removed, terminalBefore);
        }

        return Task.FromResult(removed);
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<AtomizerSchedule>> GetSchedulesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            return Task.FromResult((IReadOnlyList<AtomizerSchedule>)_schedules.Values.ToList());
        }
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<AtomizerActiveServer>> GetActiveServersAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            return Task.FromResult((IReadOnlyList<AtomizerActiveServer>)_activeServers.Values.ToList());
        }
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<QueueStats>> GetQueueStatsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var groups = _jobs
            .Values.GroupBy(j => j.QueueKey)
            .Select(g => new QueueStats
            {
                QueueKey = g.Key,
                Pending = g.Count(j => j.Status == AtomizerJobStatus.Pending),
                Processing = g.Count(j => j.Status == AtomizerJobStatus.Processing),
                Completed = g.Count(j => j.Status == AtomizerJobStatus.Completed),
                Failed = g.Count(j => j.Status == AtomizerJobStatus.Failed),
                Cancelled = g.Count(j => j.Status == AtomizerJobStatus.Cancelled),
            })
            .ToList();

        return Task.FromResult<IReadOnlyList<QueueStats>>(groups);
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
            .Values.Where(j =>
                j.Status == AtomizerJobStatus.Completed
                || j.Status == AtomizerJobStatus.Failed
                || j.Status == AtomizerJobStatus.Cancelled
            )
            .OrderByDescending(j => j.UpdatedAt)
            .ToList();

        var toRemove = terminal.Skip(retain).ToList();
        if (toRemove.Count == 0)
            return;

        var removed = RemoveJobs(toRemove);

        if (removed > 0)
        {
            _logger.LogDebug(
                "Evicted {Count} terminal jobs; retaining {Retain} most-recent terminal jobs",
                removed,
                retain
            );
        }
    }

    private static DateTimeOffset? GetTerminalTimestamp(AtomizerJob job)
    {
        return job.Status switch
        {
            AtomizerJobStatus.Completed => job.CompletedAt ?? job.UpdatedAt,
            AtomizerJobStatus.Failed => job.FailedAt ?? job.UpdatedAt,
            AtomizerJobStatus.Cancelled => job.UpdatedAt,
            _ => null,
        };
    }

    private int RemoveJobs(IEnumerable<AtomizerJob> jobs)
    {
        var removed = 0;

        foreach (var job in jobs)
        {
            UnindexFromQueue(job);

            var leaseToken = job.LeaseToken?.Token;
            if (!string.IsNullOrEmpty(leaseToken) && _leasesByToken.TryGetValue(leaseToken!, out var set))
            {
                set.TryRemove(job.Id, out _);
                if (set.IsEmpty)
                {
                    _leasesByToken.TryRemove(leaseToken!, out _);
                }
            }

            if (_jobs.TryRemove(job.Id, out _))
            {
                removed++;
            }
        }

        return removed;
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
