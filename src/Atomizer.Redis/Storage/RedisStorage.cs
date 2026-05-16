using System.Diagnostics;
using Atomizer.Abstractions;
using Atomizer.Core;
using Atomizer.Redis.Serialization;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Atomizer.Redis.Storage;

internal sealed class RedisStorage : IAtomizerStorage, IDisposable
{
    private const string JobIdFormat = "N";

    private readonly IConnectionMultiplexer _connection;
    private readonly RedisJobStorageOptions _options;
    private readonly RedisStorageKeys _keys;
    private readonly IAtomizerClock _clock;
    private readonly ILogger<RedisStorage> _logger;
    private readonly bool _ownsConnection;

    public RedisStorage(
        IConnectionMultiplexer connection,
        RedisJobStorageOptions options,
        IAtomizerClock clock,
        ILogger<RedisStorage> logger,
        bool ownsConnection = false
    )
    {
        _connection = connection;
        _options = options;
        _clock = clock;
        _logger = logger;
        _ownsConnection = ownsConnection;

        _options.Validate();
        _keys = new RedisStorageKeys(_options.KeyPrefix);
    }

    private IDatabase Database => _connection.GetDatabase(_options.Database);

    public async Task<Guid> InsertAsync(AtomizerJob job, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var lockValue = await AcquireLockAsync(_keys.InsertLock, wait: true, cancellationToken);
        try
        {
            if (job.IdempotencyKey is not null)
            {
                var existingId = await Database.StringGetAsync(_keys.Idempotency(job.IdempotencyKey));
                if (existingId.HasValue && TryParseJobId(existingId, out var parsedExistingId))
                {
                    var existing = await ReadJobAsync(parsedExistingId, cancellationToken);
                    if (existing is not null)
                    {
                        job.SequenceNumber = existing.SequenceNumber;
                        return existing.Id;
                    }
                }
            }

            if (job.PartitionKey is not null)
            {
                job.SequenceNumber = await Database.StringIncrementAsync(
                    _keys.PartitionSequence(job.QueueKey, job.PartitionKey)
                );
            }

            await StoreJobAsync(job, cancellationToken);

            if (job.IdempotencyKey is not null)
                await Database.StringSetAsync(_keys.Idempotency(job.IdempotencyKey), job.Id.ToString(JobIdFormat));

            _logger.LogDebug(
                "Inserted job {JobId} into Redis queue {QueueKey} with ScheduledAt={ScheduledAt:o}",
                job.Id,
                job.QueueKey,
                job.ScheduledAt
            );

            return job.Id;
        }
        finally
        {
            await ReleaseLockAsync(_keys.InsertLock, lockValue);
        }
    }

    public async Task UpdateJobsAsync(IEnumerable<AtomizerJob> jobs, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var jobsList = jobs.ToList();
        var lockValue = await AcquireLockAsync(_keys.InsertLock, wait: true, cancellationToken);
        try
        {
            foreach (var job in jobsList)
            {
                var existing = await ReadJobAsync(job.Id, cancellationToken);
                if (existing is null)
                {
                    throw new InvalidOperationException(
                        $"Update requested for job {job.Id} that no longer exists in Redis storage."
                    );
                }

                await StoreJobAsync(job, cancellationToken);
                await UpdateLeaseIndexAsync(existing, job);
            }

            _logger.LogDebug("Updated {Count} Redis jobs", jobsList.Count);
        }
        finally
        {
            await ReleaseLockAsync(_keys.InsertLock, lockValue);
        }
    }

    public async Task<IReadOnlyList<AtomizerJob>> GetDueJobsAsync(
        QueueKey queueKey,
        DateTimeOffset now,
        int batchSize,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var queueJobs = (await ReadAllJobsAsync(cancellationToken)).Where(job => job.QueueKey == queueKey).ToList();
        if (queueJobs.Count == 0 || batchSize <= 0)
            return Array.Empty<AtomizerJob>();

        var blockedPartitions = new HashSet<string>(
            queueJobs.Where(job => job.IsPartitionBlocked).Select(job => job.PartitionKey!.Key),
            StringComparer.Ordinal
        );

        var eligible = queueJobs.Where(job =>
            (
                job.Status == AtomizerJobStatus.Pending
                && (job.VisibleAt is null || job.VisibleAt <= now)
                && job.ScheduledAt <= now
            ) || (job.Status == AtomizerJobStatus.Processing && job.VisibleAt <= now)
        );

        eligible = eligible.Where(job => job.PartitionKey is null || !blockedPartitions.Contains(job.PartitionKey.Key));

        var unpartitionedCandidates = eligible
            .Where(job => job.PartitionKey is null)
            .Select(job => new DueJobCandidate(job, job.ScheduledAt, job.CreatedAt, string.Empty, long.MaxValue));

        var partitionedCandidates = eligible
            .Where(job => job.PartitionKey is not null)
            .GroupBy(job => job.PartitionKey!.Key, StringComparer.Ordinal)
            .SelectMany(group =>
            {
                var orderedJobs = group
                    .OrderBy(job => job.SequenceNumber ?? long.MaxValue)
                    .ThenBy(job => job.ScheduledAt)
                    .ThenBy(job => job.CreatedAt)
                    .ThenBy(job => job.Id)
                    .ToList();
                var head = orderedJobs[0];

                return orderedJobs.Select(job => new DueJobCandidate(
                    job,
                    head.ScheduledAt,
                    head.CreatedAt,
                    group.Key,
                    job.SequenceNumber ?? long.MaxValue
                ));
            });

        return unpartitionedCandidates
            .Concat(partitionedCandidates)
            .OrderBy(candidate => candidate.SortScheduledAt)
            .ThenBy(candidate => candidate.SortCreatedAt)
            .ThenBy(candidate => candidate.SortPartitionKey, StringComparer.Ordinal)
            .ThenBy(candidate => candidate.SortSequenceNumber)
            .ThenBy(candidate => candidate.Job.ScheduledAt)
            .ThenBy(candidate => candidate.Job.CreatedAt)
            .ThenBy(candidate => candidate.Job.Id)
            .Select(candidate => candidate.Job)
            .Take(batchSize)
            .ToList();
    }

    public async Task<int> ReleaseLeasedAsync(
        LeaseToken leaseToken,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var lockValue = await AcquireLockAsync(_keys.InsertLock, wait: true, cancellationToken);
        try
        {
            var leasedIds = await Database.SetMembersAsync(_keys.LeaseSet(leaseToken.Token));
            var released = 0;

            foreach (var leasedId in leasedIds)
            {
                if (!TryParseJobId(leasedId, out var jobId))
                    continue;

                var job = await ReadJobAsync(jobId, cancellationToken);
                if (job?.LeaseToken?.Token != leaseToken.Token || job.Status != AtomizerJobStatus.Processing)
                    continue;

                var existing = CloneJob(job);
                job.Release(now);
                await StoreJobAsync(job, cancellationToken);
                await UpdateLeaseIndexAsync(existing, job);
                released++;
            }

            await Database.KeyDeleteAsync(_keys.LeaseSet(leaseToken.Token));
            return released;
        }
        finally
        {
            await ReleaseLockAsync(_keys.InsertLock, lockValue);
        }
    }

    public async Task<Guid> UpsertScheduleAsync(AtomizerSchedule schedule, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var lockValue = await AcquireLockAsync(_keys.SchedulesLock, wait: true, cancellationToken);
        try
        {
            var now = _clock.UtcNow;
            var existing = await ReadScheduleAsync(schedule.JobKey.Key, cancellationToken);
            if (existing is not null)
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
            await StoreScheduleAsync(schedule, cancellationToken);
            return schedule.Id;
        }
        finally
        {
            await ReleaseLockAsync(_keys.SchedulesLock, lockValue);
        }
    }

    public async Task UpdateSchedulesAsync(IEnumerable<AtomizerSchedule> schedules, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var schedulesList = schedules.ToList();
        var lockValue = await AcquireLockAsync(_keys.SchedulesLock, wait: true, cancellationToken);
        try
        {
            var now = _clock.UtcNow;
            foreach (var schedule in schedulesList)
            {
                var existing = await ReadScheduleAsync(schedule.JobKey.Key, cancellationToken);
                if (existing is null)
                    continue;

                schedule.UpdatedAt = now;
                await StoreScheduleAsync(schedule, cancellationToken);
            }
        }
        finally
        {
            await ReleaseLockAsync(_keys.SchedulesLock, lockValue);
        }
    }

    public async Task<IReadOnlyList<AtomizerSchedule>> GetDueSchedulesAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var jobKeys = await Database.SortedSetRangeByScoreAsync(_keys.SchedulesByNextRun, stop: Score(now));
        var schedules = await ReadSchedulesAsync(jobKeys, cancellationToken);
        return schedules
            .Where(schedule => schedule.Enabled && schedule.NextRunAt <= now)
            .OrderBy(schedule => schedule.NextRunAt)
            .ThenBy(schedule => schedule.CreatedAt)
            .ToList();
    }

    public async Task<PagedResult<AtomizerJob>> GetJobsAsync(JobQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var take = Math.Min(query.Take, 500);
        var jobs = ApplyJobQueryFilters(await ReadAllJobsAsync(cancellationToken), query, includeStatusFilter: true);
        var ordered = jobs.OrderByDescending(job => job.CreatedAt).ThenByDescending(job => job.Id).ToList();

        return new PagedResult<AtomizerJob>
        {
            Items = ordered.Skip(query.Skip).Take(take).ToList(),
            TotalCount = ordered.Count,
            Skip = query.Skip,
            Take = take,
        };
    }

    public async Task<JobStatusCounts> GetJobStatusCountsAsync(JobQuery query, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var jobs = ApplyJobQueryFilters(await ReadAllJobsAsync(cancellationToken), query, includeStatusFilter: false)
            .ToList();

        return new JobStatusCounts
        {
            Pending = jobs.Count(job => job.Status == AtomizerJobStatus.Pending),
            Processing = jobs.Count(job => job.Status == AtomizerJobStatus.Processing),
            Completed = jobs.Count(job => job.Status == AtomizerJobStatus.Completed),
            Failed = jobs.Count(job => job.Status == AtomizerJobStatus.Failed),
            Cancelled = jobs.Count(job => job.Status == AtomizerJobStatus.Cancelled),
        };
    }

    public Task<AtomizerJob?> GetJobByIdAsync(Guid id, CancellationToken cancellationToken) =>
        ReadJobAsync(id, cancellationToken);

    public async Task<IReadOnlyList<AtomizerSchedule>> GetSchedulesAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var jobKeys = await Database.SortedSetRangeByRankAsync(_keys.SchedulesByNextRun);
        return await ReadSchedulesAsync(jobKeys, cancellationToken);
    }

    public async Task<IReadOnlyList<AtomizerActiveServer>> GetActiveServersAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var instanceIds = await Database.SortedSetRangeByRankAsync(_keys.ServersByHeartbeat, order: Order.Descending);
        return await ReadServersAsync(instanceIds, cancellationToken);
    }

    public async Task<IReadOnlyList<QueueStats>> GetQueueStatsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return (await ReadAllJobsAsync(cancellationToken))
            .GroupBy(job => job.QueueKey)
            .Select(group => new QueueStats
            {
                QueueKey = group.Key,
                Pending = group.Count(job => job.Status == AtomizerJobStatus.Pending),
                Processing = group.Count(job => job.Status == AtomizerJobStatus.Processing),
                Completed = group.Count(job => job.Status == AtomizerJobStatus.Completed),
                Failed = group.Count(job => job.Status == AtomizerJobStatus.Failed),
                Cancelled = group.Count(job => job.Status == AtomizerJobStatus.Cancelled),
            })
            .ToList();
    }

    public async Task UpsertHeartbeatAsync(AtomizerActiveServer server, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(server.InstanceId))
            throw new ArgumentException("Active server instance id cannot be null or empty.", nameof(server));

        await StoreServerAsync(server, cancellationToken);
    }

    public async Task<IReadOnlyList<AtomizerActiveServer>> GetStaleServersAsync(
        DateTimeOffset staleBefore,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var instanceIds = await Database.SortedSetRangeByScoreAsync(_keys.ServersByHeartbeat, stop: Score(staleBefore));
        var servers = await ReadServersAsync(instanceIds, cancellationToken);
        return servers
            .Where(server => server.LastHeartbeatAt < staleBefore)
            .OrderBy(server => server.LastHeartbeatAt)
            .ToList();
    }

    public async Task<AtomizerHeartbeatRecoveryResult> TryRecoverStaleServerAsync(
        string instanceId,
        DateTimeOffset staleBefore,
        DateTimeOffset now,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var lockValue = await AcquireLockAsync(_keys.HeartbeatsLock, wait: true, cancellationToken);
        try
        {
            var server = await ReadServerAsync(instanceId, cancellationToken);
            if (server is null || server.LastHeartbeatAt >= staleBefore)
                return AtomizerHeartbeatRecoveryResult.NotRecovered(instanceId);

            await Database.KeyDeleteAsync(_keys.Server(instanceId));
            await Database.SortedSetRemoveAsync(_keys.ServersByHeartbeat, instanceId);

            var tokenPrefix = instanceId + LeaseToken.Delimiter;
            var released = 0;
            foreach (var job in await ReadAllJobsAsync(cancellationToken))
            {
                if (
                    job.Status != AtomizerJobStatus.Processing
                    || job.LeaseToken?.Token.StartsWith(tokenPrefix, StringComparison.Ordinal) != true
                )
                {
                    continue;
                }

                var existing = CloneJob(job);
                job.Release(now);
                await StoreJobAsync(job, cancellationToken);
                await UpdateLeaseIndexAsync(existing, job);
                released++;
            }

            return new AtomizerHeartbeatRecoveryResult(instanceId, true, released);
        }
        finally
        {
            await ReleaseLockAsync(_keys.HeartbeatsLock, lockValue);
        }
    }

    public async Task RemoveHeartbeatAsync(string instanceId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await Database.KeyDeleteAsync(_keys.Server(instanceId));
        await Database.SortedSetRemoveAsync(_keys.ServersByHeartbeat, instanceId);
    }

    public async Task<TResult> ExecuteInLeaseAsync<TResult>(
        QueueKey queue,
        Func<CancellationToken, Task<TResult>> callback,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var lockValue = await AcquireLockAsync(_keys.QueueLease(queue), wait: false, cancellationToken);
        if (lockValue is null)
            return default!;

        try
        {
            return await callback(cancellationToken);
        }
        finally
        {
            await ReleaseLockAsync(_keys.QueueLease(queue), lockValue);
        }
    }

    public Task ExecuteInLeaseAsync(
        QueueKey queue,
        Func<CancellationToken, Task> callback,
        CancellationToken cancellationToken
    ) =>
        ExecuteInLeaseAsync(
            queue,
            async ct =>
            {
                await callback(ct);
                return true;
            },
            cancellationToken
        );

    public void Dispose()
    {
        if (_ownsConnection)
            _connection.Dispose();
    }

    private async Task StoreJobAsync(AtomizerJob job, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var serialized = RedisRecordSerializer.Serialize(RedisJobRecord.FromJob(job));
        await Database.StringSetAsync(_keys.Job(job.Id), serialized);
        await Database.SortedSetAddAsync(_keys.JobsByCreated, job.Id.ToString(JobIdFormat), Score(job.CreatedAt));
    }

    private async Task<AtomizerJob?> ReadJobAsync(Guid id, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var value = await Database.StringGetAsync(_keys.Job(id));
        if (!value.HasValue)
            return null;

        return RedisRecordSerializer.Deserialize<RedisJobRecord>(value.ToString())?.ToJob();
    }

    private async Task<List<AtomizerJob>> ReadAllJobsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var ids = await Database.SortedSetRangeByRankAsync(_keys.JobsByCreated);
        if (ids.Length == 0)
            return new List<AtomizerJob>();

        var keys = ids.Where(id => TryParseJobId(id, out _))
            .Select(id =>
            {
                TryParseJobId(id, out var parsed);
                return _keys.Job(parsed);
            })
            .ToArray();

        var values = await Database.StringGetAsync(keys);
        var jobs = new List<AtomizerJob>(values.Length);
        foreach (var value in values)
        {
            if (!value.HasValue)
                continue;

            var record = RedisRecordSerializer.Deserialize<RedisJobRecord>(value.ToString());
            if (record is not null)
                jobs.Add(record.ToJob());
        }

        return jobs;
    }

    private async Task StoreScheduleAsync(AtomizerSchedule schedule, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await Database.StringSetAsync(
            _keys.Schedule(schedule.JobKey.Key),
            RedisRecordSerializer.Serialize(RedisScheduleRecord.FromSchedule(schedule))
        );
        await Database.SortedSetAddAsync(_keys.SchedulesByNextRun, schedule.JobKey.Key, Score(schedule.NextRunAt));
    }

    private async Task<AtomizerSchedule?> ReadScheduleAsync(string jobKey, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var value = await Database.StringGetAsync(_keys.Schedule(jobKey));
        if (!value.HasValue)
            return null;

        return RedisRecordSerializer.Deserialize<RedisScheduleRecord>(value.ToString())?.ToSchedule();
    }

    private async Task<IReadOnlyList<AtomizerSchedule>> ReadSchedulesAsync(
        IReadOnlyCollection<RedisValue> jobKeys,
        CancellationToken cancellationToken
    )
    {
        if (jobKeys.Count == 0)
            return Array.Empty<AtomizerSchedule>();

        var keys = jobKeys.Select(jobKey => _keys.Schedule(jobKey.ToString())).ToArray();
        var values = await Database.StringGetAsync(keys);
        var schedules = new List<AtomizerSchedule>(values.Length);
        foreach (var value in values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!value.HasValue)
                continue;

            var record = RedisRecordSerializer.Deserialize<RedisScheduleRecord>(value.ToString());
            if (record is not null)
                schedules.Add(record.ToSchedule());
        }

        return schedules;
    }

    private async Task StoreServerAsync(AtomizerActiveServer server, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        await Database.StringSetAsync(
            _keys.Server(server.InstanceId),
            RedisRecordSerializer.Serialize(RedisActiveServerRecord.FromServer(server))
        );
        await Database.SortedSetAddAsync(_keys.ServersByHeartbeat, server.InstanceId, Score(server.LastHeartbeatAt));
    }

    private async Task<AtomizerActiveServer?> ReadServerAsync(string instanceId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var value = await Database.StringGetAsync(_keys.Server(instanceId));
        if (!value.HasValue)
            return null;

        return RedisRecordSerializer.Deserialize<RedisActiveServerRecord>(value.ToString())?.ToServer();
    }

    private async Task<IReadOnlyList<AtomizerActiveServer>> ReadServersAsync(
        IReadOnlyCollection<RedisValue> instanceIds,
        CancellationToken cancellationToken
    )
    {
        if (instanceIds.Count == 0)
            return Array.Empty<AtomizerActiveServer>();

        var keys = instanceIds.Select(instanceId => _keys.Server(instanceId.ToString())).ToArray();
        var values = await Database.StringGetAsync(keys);
        var servers = new List<AtomizerActiveServer>(values.Length);
        foreach (var value in values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!value.HasValue)
                continue;

            var record = RedisRecordSerializer.Deserialize<RedisActiveServerRecord>(value.ToString());
            if (record is not null)
                servers.Add(record.ToServer());
        }

        return servers;
    }

    private async Task UpdateLeaseIndexAsync(AtomizerJob existing, AtomizerJob updated)
    {
        var existingToken = existing.LeaseToken?.Token;
        var updatedToken = updated.LeaseToken?.Token;
        var jobId = updated.Id.ToString(JobIdFormat);

        if (existingToken is not null && !string.Equals(existingToken, updatedToken, StringComparison.Ordinal))
            await Database.SetRemoveAsync(_keys.LeaseSet(existingToken), jobId);

        if (updatedToken is not null)
            await Database.SetAddAsync(_keys.LeaseSet(updatedToken), jobId);
    }

    private async Task<string?> AcquireLockAsync(RedisKey key, bool wait, CancellationToken cancellationToken)
    {
        var value = Guid.NewGuid().ToString(JobIdFormat);
        var stopwatch = Stopwatch.StartNew();

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await Database.LockTakeAsync(key, value, _options.LockExpiry))
                return value;

            if (!wait)
                return null;

            if (stopwatch.Elapsed >= _options.LockAcquisitionTimeout)
                throw new TimeoutException($"Timed out acquiring Redis lock '{key}'.");

            await Task.Delay(_options.LockRetryDelay, cancellationToken);
        }
    }

    private Task ReleaseLockAsync(RedisKey key, string? value)
    {
        if (value is null)
            return Task.CompletedTask;

        return Database.LockReleaseAsync(key, value);
    }

    private static IEnumerable<AtomizerJob> ApplyJobQueryFilters(
        IEnumerable<AtomizerJob> jobs,
        JobQuery query,
        bool includeStatusFilter
    )
    {
        if (includeStatusFilter && query.Statuses is { Count: > 0 })
            jobs = jobs.Where(job => query.Statuses.Contains(job.Status));

        if (query.QueueKey is not null)
            jobs = jobs.Where(job => job.QueueKey == query.QueueKey);

        if (query.PayloadTypeName is not null)
        {
            jobs = jobs.Where(job =>
                job.PayloadType is not null
                && job.PayloadType.Name.IndexOf(query.PayloadTypeName, StringComparison.OrdinalIgnoreCase) >= 0
            );
        }

        if (query.CreatedFromUtc.HasValue)
            jobs = jobs.Where(job => job.CreatedAt >= query.CreatedFromUtc.Value);

        if (query.CreatedToUtc.HasValue)
            jobs = jobs.Where(job => job.CreatedAt <= query.CreatedToUtc.Value);

        return jobs;
    }

    private static bool TryParseJobId(RedisValue value, out Guid id)
    {
        var text = value.ToString();
        return Guid.TryParseExact(text, JobIdFormat, out id) || Guid.TryParse(text, out id);
    }

    private static double Score(DateTimeOffset value) => value.ToUniversalTime().ToUnixTimeMilliseconds();

    private static AtomizerJob CloneJob(AtomizerJob job) => RedisJobRecord.FromJob(job).ToJob();

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
}
