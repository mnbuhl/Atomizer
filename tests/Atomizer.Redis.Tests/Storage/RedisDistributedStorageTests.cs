using Atomizer.Core;
using Atomizer.Redis.Storage;
using Atomizer.Tests.Utilities.TestJobs;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Atomizer.Redis.Tests.Storage;

[Collection(nameof(RedisStorageFixture))]
public sealed class RedisDistributedStorageTests : IAsyncLifetime
{
    private readonly RedisStorageFixture _fixture;
    private readonly IAtomizerClock _clock = Substitute.For<IAtomizerClock>();
    private readonly DateTimeOffset _now = new DateTimeOffset(2024, 1, 15, 12, 0, 0, TimeSpan.Zero);
    private string _keyPrefix = null!;

    public RedisDistributedStorageTests(RedisStorageFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync()
    {
        await _fixture.FlushAsync();
        _clock.UtcNow.Returns(_now);
        _keyPrefix = $"distributed:{Guid.NewGuid():N}";
    }

    public async ValueTask DisposeAsync()
    {
        await _fixture.FlushAsync();
    }

    [Fact]
    public async Task Operations_WhenDifferentStorageInstancesShareRedis_ShouldObserveSameJobState()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var writer = CreateStorage();
        var worker = CreateStorage();
        var job = CreateJob();
        var leaseToken = CreateLeaseToken("server-a", QueueKey.Default);

        await writer.InsertAsync(job, cancellationToken);
        var due = await worker.GetDueJobsAsync(QueueKey.Default, _now, 10, cancellationToken);

        due.Should().ContainSingle();
        var leased = due[0];
        leased.Lease(leaseToken, _now, TimeSpan.FromMinutes(5));
        await worker.UpdateJobsAsync(new[] { leased }, cancellationToken);

        var observedProcessing = await writer.GetJobByIdAsync(job.Id, cancellationToken);
        observedProcessing!.Status.Should().Be(AtomizerJobStatus.Processing);
        observedProcessing.LeaseToken.Should().Be(leaseToken);

        var released = await writer.ReleaseLeasedAsync(leaseToken, _now.AddMinutes(1), cancellationToken);

        released.Should().Be(1);
        var observedReleased = await worker.GetJobByIdAsync(job.Id, cancellationToken);
        observedReleased!.Status.Should().Be(AtomizerJobStatus.Pending);
        observedReleased.LeaseToken.Should().BeNull();
        observedReleased.VisibleAt.Should().BeNull();
    }

    [Fact]
    public async Task ExecuteInLeaseAsync_WhenSameQueueLeaseIsHeldByDifferentInstance_ShouldSkipSecondCallback()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var firstStorage = CreateStorage();
        var secondStorage = CreateStorage();
        var started = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondCallbackInvoked = false;

        var first = firstStorage.ExecuteInLeaseAsync(
            QueueKey.Default,
            async _ =>
            {
                started.SetResult(null);
                await release.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                return 42;
            },
            cancellationToken
        );

        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

        int second;
        try
        {
            second = await secondStorage.ExecuteInLeaseAsync(
                QueueKey.Default,
                _ =>
                {
                    secondCallbackInvoked = true;
                    return Task.FromResult(10);
                },
                cancellationToken
            );
        }
        finally
        {
            release.SetResult(null);
        }

        var firstResult = await first.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

        second.Should().Be(0);
        firstResult.Should().Be(42);
        secondCallbackInvoked.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteInLeaseAsync_WhenDifferentQueueLeaseIsHeldByDifferentInstance_ShouldRunCallback()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var firstStorage = CreateStorage();
        var secondStorage = CreateStorage();
        var otherQueue = new QueueKey("other");
        var started = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);

        var first = firstStorage.ExecuteInLeaseAsync(
            QueueKey.Default,
            async _ =>
            {
                started.SetResult(null);
                await release.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
                return 42;
            },
            cancellationToken
        );

        await started.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

        int second;
        try
        {
            second = await secondStorage.ExecuteInLeaseAsync(otherQueue, _ => Task.FromResult(10), cancellationToken);
        }
        finally
        {
            release.SetResult(null);
        }

        var firstResult = await first.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);

        second.Should().Be(10);
        firstResult.Should().Be(42);
    }

    [Fact]
    public async Task InsertAsync_WhenIdempotencyKeyIsInsertedFromDifferentInstance_ShouldReturnExistingJob()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var firstStorage = CreateStorage();
        var secondStorage = CreateStorage();
        var partition = new PartitionKey("customer-1");
        var firstJob = CreateJob(idempotencyKey: "idem-1", partitionKey: partition);
        var duplicate = CreateJob(idempotencyKey: "idem-1", partitionKey: partition);

        var firstId = await firstStorage.InsertAsync(firstJob, cancellationToken);
        var duplicateId = await secondStorage.InsertAsync(duplicate, cancellationToken);
        var jobs = await secondStorage.GetJobsAsync(new JobQuery { Take = 10 }, cancellationToken);

        duplicateId.Should().Be(firstId);
        duplicate.SequenceNumber.Should().Be(firstJob.SequenceNumber);
        jobs.TotalCount.Should().Be(1);
        jobs.Items.Should().ContainSingle(stored => stored.Id == firstId);
    }

    [Fact]
    public async Task InsertAsync_WhenSamePartitionIsUsedAcrossQueuesAndInstances_ShouldAssignSequencesPerQueue()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var firstStorage = CreateStorage();
        var secondStorage = CreateStorage();
        var partition = new PartitionKey("tenant-1");
        var otherQueue = new QueueKey("other");
        var defaultQueueFirst = CreateJob(partitionKey: partition);
        var otherQueueFirst = CreateJob(queueKey: otherQueue, partitionKey: partition);
        var defaultQueueSecond = CreateJob(partitionKey: partition);

        await firstStorage.InsertAsync(defaultQueueFirst, cancellationToken);
        await secondStorage.InsertAsync(otherQueueFirst, cancellationToken);
        await secondStorage.InsertAsync(defaultQueueSecond, cancellationToken);

        defaultQueueFirst.SequenceNumber.Should().Be(1);
        otherQueueFirst.SequenceNumber.Should().Be(1);
        defaultQueueSecond.SequenceNumber.Should().Be(2);
    }

    [Fact]
    public async Task DeleteExpiredJobsAsync_WhenExpiredJobHadIdempotencyKey_ShouldAllowKeyToBeReused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var firstStorage = CreateStorage();
        var secondStorage = CreateStorage();
        var expired = CreateJob(idempotencyKey: "expiring-idempotency", createdAt: _now.AddDays(-10));
        var cutoff = _now.AddDays(-1);
        var terminalAt = cutoff.AddTicks(-1);

        await firstStorage.InsertAsync(expired, cancellationToken);
        expired.Lease(CreateLeaseToken("server-a", QueueKey.Default), _now.AddDays(-10), TimeSpan.FromMinutes(5));
        expired.MarkAsCompleted(terminalAt);
        await firstStorage.UpdateJobsAsync(new[] { expired }, cancellationToken);

        var deleted = await secondStorage.DeleteExpiredJobsAsync(cutoff, cancellationToken);
        var replacement = CreateJob(idempotencyKey: "expiring-idempotency");
        var replacementId = await secondStorage.InsertAsync(replacement, cancellationToken);

        deleted.Should().Be(1);
        replacementId.Should().NotBe(expired.Id);
        replacementId.Should().Be(replacement.Id);
        var jobs = await firstStorage.GetJobsAsync(new JobQuery { Take = 10 }, cancellationToken);
        jobs.Items.Should().ContainSingle(job => job.Id == replacement.Id);
    }

    [Fact]
    public async Task DeleteExpiredJobsAsync_WhenExpiredJobsAreDeleted_ShouldRemoveJobsFromDashboardIndexes()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var storage = CreateStorage();
        var cutoff = _now.AddDays(-7);
        var expired = CreateJob(createdAt: cutoff.AddDays(-1));
        var retained = CreateJob(createdAt: cutoff.AddDays(-1));
        var pending = CreateJob(createdAt: cutoff.AddDays(-30));

        await storage.InsertAsync(expired, cancellationToken);
        await storage.InsertAsync(retained, cancellationToken);
        await storage.InsertAsync(pending, cancellationToken);

        expired.Lease(CreateLeaseToken("server-a", QueueKey.Default), _now.AddDays(-8), TimeSpan.FromMinutes(5));
        expired.MarkAsCompleted(cutoff.AddTicks(-1));
        retained.Lease(CreateLeaseToken("server-a", QueueKey.Default), _now.AddDays(-8), TimeSpan.FromMinutes(5));
        retained.MarkAsCompleted(cutoff);
        await storage.UpdateJobsAsync(new[] { expired, retained }, cancellationToken);

        var deleted = await storage.DeleteExpiredJobsAsync(cutoff, cancellationToken);

        var jobs = await storage.GetJobsAsync(
            new JobQuery { QueueKey = QueueKey.Default, Take = 10 },
            cancellationToken
        );
        var counts = await storage.GetJobStatusCountsAsync(
            new JobQuery { QueueKey = QueueKey.Default },
            cancellationToken
        );
        var stats = await storage.GetQueueStatsAsync(cancellationToken);

        deleted.Should().Be(1);
        jobs.TotalCount.Should().Be(2);
        jobs.Items.Should().NotContain(job => job.Id == expired.Id);
        counts.Pending.Should().Be(1);
        counts.Completed.Should().Be(1);

        var queueStats = stats.Single(stat => stat.QueueKey == QueueKey.Default);
        queueStats.Pending.Should().Be(1);
        queueStats.Completed.Should().Be(1);
    }

    [Fact]
    public async Task TryRecoverStaleServerAsync_WhenMultipleServersHaveProcessingJobs_ShouldReleaseOnlyRequestedServer()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var writer = CreateStorage();
        var recoverer = CreateStorage();
        var serverAToken = CreateLeaseToken("server-a", QueueKey.Default);
        var serverBToken = CreateLeaseToken("server-b", QueueKey.Default);
        var serverAJob = CreateJob();
        var serverBJob = CreateJob();

        await writer.InsertAsync(serverAJob, cancellationToken);
        await writer.InsertAsync(serverBJob, cancellationToken);
        serverAJob.Lease(serverAToken, _now.AddMinutes(-10), TimeSpan.FromMinutes(5));
        serverBJob.Lease(serverBToken, _now.AddMinutes(-10), TimeSpan.FromMinutes(5));
        await writer.UpdateJobsAsync(new[] { serverAJob, serverBJob }, cancellationToken);
        await writer.UpsertHeartbeatAsync(
            new AtomizerActiveServer { InstanceId = "server-a", LastHeartbeatAt = _now.AddMinutes(-10) },
            cancellationToken
        );
        await writer.UpsertHeartbeatAsync(
            new AtomizerActiveServer { InstanceId = "server-b", LastHeartbeatAt = _now.AddMinutes(-10) },
            cancellationToken
        );

        var result = await recoverer.TryRecoverStaleServerAsync(
            "server-a",
            _now.AddMinutes(-5),
            _now,
            cancellationToken
        );

        result.Recovered.Should().BeTrue();
        result.ReleasedJobCount.Should().Be(1);

        var recoveredJob = await writer.GetJobByIdAsync(serverAJob.Id, cancellationToken);
        var preservedJob = await writer.GetJobByIdAsync(serverBJob.Id, cancellationToken);
        recoveredJob!.Status.Should().Be(AtomizerJobStatus.Pending);
        recoveredJob.LeaseToken.Should().BeNull();
        preservedJob!.Status.Should().Be(AtomizerJobStatus.Processing);
        preservedJob.LeaseToken.Should().Be(serverBToken);

        var servers = await writer.GetActiveServersAsync(cancellationToken);
        servers.Should().NotContain(server => server.InstanceId == "server-a");
        servers.Should().ContainSingle(server => server.InstanceId == "server-b");
    }

    [Fact]
    public async Task UpsertScheduleAsync_WhenScheduleExistsFromDifferentInstance_ShouldReuseIdentityAndUpdateDefinition()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var firstStorage = CreateStorage();
        var secondStorage = CreateStorage();
        var firstSchedule = CreateSchedule("original");
        var updatedSchedule = CreateSchedule("updated");

        var firstId = await firstStorage.UpsertScheduleAsync(firstSchedule, cancellationToken);
        var updatedId = await secondStorage.UpsertScheduleAsync(updatedSchedule, cancellationToken);

        var schedules = await firstStorage.GetSchedulesAsync(cancellationToken);
        var stored = schedules.Should().ContainSingle(schedule => schedule.JobKey == firstSchedule.JobKey).Subject;
        updatedId.Should().Be(firstId);
        stored.Id.Should().Be(firstId);
        stored.Payload.Should().Be("updated");
        stored.CreatedAt.Should().Be(firstSchedule.CreatedAt);
    }

    private RedisStorage CreateStorage() =>
        new RedisStorage(
            _fixture.Connection,
            new RedisJobStorageOptions { KeyPrefix = _keyPrefix },
            _clock,
            NullLogger<RedisStorage>.Instance
        );

    private AtomizerJob CreateJob(
        QueueKey? queueKey = null,
        PartitionKey? partitionKey = null,
        string? idempotencyKey = null,
        DateTimeOffset? createdAt = null,
        DateTimeOffset? scheduledAt = null
    )
    {
        var created = createdAt ?? _now;
        return AtomizerJob.Create(
            queueKey ?? QueueKey.Default,
            typeof(WriteLineJob),
            "{}",
            created,
            scheduledAt ?? created,
            idempotencyKey: idempotencyKey,
            partitionKey: partitionKey
        );
    }

    private AtomizerSchedule CreateSchedule(string payload) =>
        AtomizerSchedule.Create(
            new JobKey("distributed-schedule"),
            QueueKey.Default,
            typeof(WriteLineJob),
            payload,
            Schedule.Default,
            TimeZoneInfo.Utc,
            _now
        );

    private static LeaseToken CreateLeaseToken(string instanceId, QueueKey queueKey) =>
        new LeaseToken($"{instanceId}{LeaseToken.Delimiter}{queueKey.Key}{LeaseToken.Delimiter}{Guid.NewGuid():N}");
}
