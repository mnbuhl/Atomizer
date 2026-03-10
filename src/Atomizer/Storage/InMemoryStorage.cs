using System.Collections.Concurrent;
using Atomizer.Abstractions;
using Atomizer.Core;
using Microsoft.Extensions.Logging;

namespace Atomizer.Storage;

/// <summary>
/// An in-memory implementation of <see cref="IAtomizerDashboardStorage"/> backed by concurrent
/// dictionaries. Suitable for development, testing, and single-instance deployments.
/// </summary>
public sealed class InMemoryStorage : IAtomizerDashboardStorage
{
    private readonly ConcurrentDictionary<Guid, AtomizerJob> _jobs = new();
    private readonly Dictionary<QueueKey, HashSet<Guid>> _queues = new(); // guarded per-queue
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<Guid, byte>> _leasesByToken = new();

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

    public Task UpdateJobsAsync(IEnumerable<AtomizerJob> jobs, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var jobsList = jobs.ToList();

        foreach (var job in jobsList)
        {
            if (!_jobs.TryGetValue(job.Id, out _))
            {
                _logger.LogError("Update requested for missing job {JobId}", job.Id);
                continue;
            }

            _jobs[job.Id] = job;

            UpdateLease(job);
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

        List<AtomizerJob> candidates;

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

        _logger.LogDebug(
            "LeaseBatch: leased {Count} jobs from queue {QueueKey}: [{Ids}]",
            candidates.Count,
            queueKey,
            string.Join(",", candidates.Select(c => c.Id))
        );

        return Task.FromResult((IReadOnlyList<AtomizerJob>)candidates);
    }

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

    public Task<Guid> UpsertScheduleAsync(AtomizerSchedule schedule, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = _clock.UtcNow;

        schedule.CreatedAt = schedule.CreatedAt == default ? now : schedule.CreatedAt;
        schedule.UpdatedAt = now;
        _schedules[schedule.JobKey] = schedule;

        _logger.LogDebug("UpsertSchedule: upserted schedule for jobKey={JobKey}", schedule.JobKey);
        return Task.FromResult(schedule.Id);
    }

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

    /// <inheritdoc />
    public Task<IReadOnlyList<QueueStats>> GetQueueStatsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var stats = _jobs
            .Values.GroupBy(j => j.QueueKey)
            .Select(g => new QueueStats(
                g.Key,
                g.Count(j => j.Status == AtomizerJobStatus.Pending),
                g.Count(j => j.Status == AtomizerJobStatus.Processing),
                g.Count(j => j.Status == AtomizerJobStatus.Completed),
                g.Count(j => j.Status == AtomizerJobStatus.Failed)
            ))
            .OrderBy(s => s.QueueKey.Key)
            .ToList();

        return Task.FromResult((IReadOnlyList<QueueStats>)stats);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AtomizerJob>> GetRecentJobsAsync(int skip, int take, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var jobs = _jobs.Values.OrderByDescending(j => j.CreatedAt).Skip(skip).Take(take).ToList();

        return Task.FromResult((IReadOnlyList<AtomizerJob>)jobs);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AtomizerSchedule>> GetAllSchedulesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var schedules = _schedules.Values.OrderBy(s => s.JobKey.Key).ToList();

        return Task.FromResult((IReadOnlyList<AtomizerSchedule>)schedules);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<AtomizerJob>> GetJobsAsync(
        JobFilter filter,
        int skip,
        int take,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var query = _jobs.Values.AsEnumerable();

        if (filter.QueueName is not null)
        {
            query = query.Where(j => j.QueueKey.Key.Contains(filter.QueueName, StringComparison.OrdinalIgnoreCase));
        }

        if (filter.Status is not null)
        {
            query = query.Where(j => j.Status == filter.Status);
        }

        var result = query.OrderByDescending(j => j.CreatedAt).Skip(skip).Take(take).ToList();

        return Task.FromResult<IReadOnlyList<AtomizerJob>>(result);
    }

    /// <inheritdoc />
    public Task<AtomizerJob?> GetJobAsync(Guid jobId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _jobs.TryGetValue(jobId, out var job);
        return Task.FromResult<AtomizerJob?>(job);
    }

    /// <inheritdoc />
    public Task<AtomizerJob?> GetJobByIdAsync(Guid jobId, CancellationToken cancellationToken) =>
        GetJobAsync(jobId, cancellationToken);

    /// <inheritdoc />
    public Task<AtomizerJob?> GetLastJobForScheduleAsync(JobKey jobKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var job = _jobs
            .Values.Where(j => j.ScheduleJobKey != null && j.ScheduleJobKey.Equals(jobKey))
            .OrderByDescending(j => j.UpdatedAt)
            .FirstOrDefault();

        return Task.FromResult<AtomizerJob?>(job);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ScheduleRecord>> GetSchedulesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var records = _schedules.Values.Select(ToScheduleRecord).OrderBy(r => r.JobKey).ToList();

        return Task.FromResult((IReadOnlyList<ScheduleRecord>)records);
    }

    /// <inheritdoc />
    public Task<AtomizerStats> GetStatsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var jobs = _jobs.Values.ToList();

        var queues = jobs.GroupBy(j => j.QueueKey)
            .Select(g => new QueueStats(
                g.Key,
                g.Count(j => j.Status == AtomizerJobStatus.Pending),
                g.Count(j => j.Status == AtomizerJobStatus.Processing),
                g.Count(j => j.Status == AtomizerJobStatus.Completed),
                g.Count(j => j.Status == AtomizerJobStatus.Failed)
            ))
            .OrderBy(s => s.QueueKey.Key)
            .ToList();

        var totalErrors = jobs.Sum(j => j.Errors.Count);
        var totalSchedules = _schedules.Count;
        var enabledSchedules = _schedules.Values.Count(s => s.Enabled);

        var stats = new AtomizerStats(
            Queues: queues,
            TotalPending: jobs.Count(j => j.Status == AtomizerJobStatus.Pending),
            TotalProcessing: jobs.Count(j => j.Status == AtomizerJobStatus.Processing),
            TotalCompleted: jobs.Count(j => j.Status == AtomizerJobStatus.Completed),
            TotalFailed: jobs.Count(j => j.Status == AtomizerJobStatus.Failed),
            TotalCancelled: jobs.Count(j => j.Status == AtomizerJobStatus.Cancelled),
            TotalErrors: totalErrors,
            TotalSchedules: totalSchedules,
            EnabledSchedules: enabledSchedules,
            GeneratedAt: _clock.UtcNow
        );

        return Task.FromResult(stats);
    }

    // ---- helpers ----

    private static ScheduleRecord ToScheduleRecord(AtomizerSchedule schedule) =>
        new ScheduleRecord(
            Id: schedule.Id,
            JobKey: schedule.JobKey.Key,
            QueueKey: schedule.QueueKey.Key,
            CronExpression: schedule.Schedule.ToString(),
            Enabled: schedule.Enabled,
            MisfirePolicy: schedule.MisfirePolicy,
            NextRunAt: schedule.NextRunAt,
            LastEnqueueAt: schedule.LastEnqueueAt,
            CreatedAt: schedule.CreatedAt,
            UpdatedAt: schedule.UpdatedAt,
            PayloadTypeName: schedule.PayloadType?.FullName,
            TimeZoneId: schedule.TimeZone.Id
        );

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
