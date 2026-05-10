using Atomizer.Core;
using Atomizer.Dashboard;
using Atomizer.Dashboard.Storage;
using Atomizer.Storage;

namespace Atomizer.Tests.Dashboard;

public class InMemoryDashboardStorageTests
{
    private readonly IAtomizerClock _clock = Substitute.For<IAtomizerClock>();
    private readonly TestableLogger<InMemoryStorage> _storageLogger = Substitute.For<TestableLogger<InMemoryStorage>>();
    private readonly InMemoryStorage _inMemoryStorage;
    private readonly InMemoryDashboardStorage _sut;
    private readonly DateTimeOffset _now = new DateTimeOffset(2024, 1, 15, 12, 0, 0, TimeSpan.Zero);

    public InMemoryDashboardStorageTests()
    {
        _clock.UtcNow.Returns(_now);
        _inMemoryStorage = new InMemoryStorage(
            new InMemoryJobStorageOptions { AmountOfJobsToRetainInMemory = 1000 },
            _clock,
            _storageLogger
        );
        _sut = new InMemoryDashboardStorage(_inMemoryStorage);
    }

    private async Task<AtomizerJob> InsertJobAsync(
        QueueKey? queue = null,
        AtomizerJobStatus status = AtomizerJobStatus.Pending,
        Type? payloadType = null,
        DateTimeOffset? createdAt = null
    )
    {
        var created = createdAt ?? _now;
        var job = AtomizerJob.Create(
            queue ?? QueueKey.Default,
            payloadType ?? typeof(string),
            "payload",
            created,
            created
        );

        await _inMemoryStorage.InsertAsync(job, CancellationToken.None);

        if (status == AtomizerJobStatus.Processing)
        {
            var leaseToken = new LeaseToken($"server-1:*:{job.QueueKey.Key}:*:{Guid.NewGuid()}");
            job.Lease(leaseToken, _now, TimeSpan.FromMinutes(5));
            await _inMemoryStorage.UpdateJobsAsync([job], CancellationToken.None);
        }
        else if (status == AtomizerJobStatus.Completed)
        {
            var leaseToken = new LeaseToken($"server-1:*:{job.QueueKey.Key}:*:{Guid.NewGuid()}");
            job.Lease(leaseToken, _now, TimeSpan.FromMinutes(5));
            job.Attempt();
            job.MarkAsCompleted(_now);
            await _inMemoryStorage.UpdateJobsAsync([job], CancellationToken.None);
        }
        else if (status == AtomizerJobStatus.Failed)
        {
            var leaseToken = new LeaseToken($"server-1:*:{job.QueueKey.Key}:*:{Guid.NewGuid()}");
            job.Lease(leaseToken, _now, TimeSpan.FromMinutes(5));
            job.Attempt();
            job.MarkAsFailed(_now);
            await _inMemoryStorage.UpdateJobsAsync([job], CancellationToken.None);
        }

        return job;
    }

    [Fact]
    public async Task GetJobsAsync_WhenStoreIsEmpty_ShouldReturnEmptyResult()
    {
        var result = await _sut.GetJobsAsync(new JobQuery(), CancellationToken.None);

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(0);
    }

    [Fact]
    public async Task GetJobsAsync_WhenStatusFilterApplied_ShouldReturnOnlyMatchingJobs()
    {
        await InsertJobAsync(status: AtomizerJobStatus.Pending);
        await InsertJobAsync(status: AtomizerJobStatus.Processing);

        var result = await _sut.GetJobsAsync(
            new JobQuery { Statuses = [AtomizerJobStatus.Pending] },
            CancellationToken.None
        );

        result.Items.Should().AllSatisfy(j => j.Status.Should().Be(AtomizerJobStatus.Pending));
        result.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task GetJobsAsync_WhenQueueFilterApplied_ShouldReturnOnlyMatchingJobs()
    {
        var otherQueue = new QueueKey("other");
        await InsertJobAsync(queue: QueueKey.Default);
        await InsertJobAsync(queue: otherQueue);

        var result = await _sut.GetJobsAsync(new JobQuery { QueueKey = QueueKey.Default }, CancellationToken.None);

        result.Items.Should().AllSatisfy(j => j.QueueKey.Should().Be(QueueKey.Default));
        result.TotalCount.Should().Be(1);
    }

    [Fact]
    public async Task GetJobsAsync_WhenPayloadTypeNameFilterApplied_ShouldMatchCaseInsensitiveSubstring()
    {
        await InsertJobAsync(payloadType: typeof(string));
        await InsertJobAsync(payloadType: typeof(int));

        var result = await _sut.GetJobsAsync(new JobQuery { PayloadTypeName = "STR" }, CancellationToken.None);

        result.Items.Should().HaveCount(1);
        result.Items[0].PayloadType.Should().Be(typeof(string));
    }

    [Fact]
    public async Task GetJobsAsync_WhenDateRangeFilterApplied_ShouldReturnJobsWithinRange()
    {
        var early = _now.AddHours(-2);
        var late = _now.AddHours(2);

        await InsertJobAsync(createdAt: early);
        await InsertJobAsync(createdAt: _now);
        await InsertJobAsync(createdAt: late);

        var result = await _sut.GetJobsAsync(
            new JobQuery { CreatedFromUtc = _now.AddHours(-1), CreatedToUtc = _now.AddHours(1) },
            CancellationToken.None
        );

        result.TotalCount.Should().Be(1);
        result.Items[0].CreatedAt.Should().Be(_now);
    }

    [Fact]
    public async Task GetJobsAsync_WhenMultipleFiltersApplied_ShouldApplyAllFilters()
    {
        var otherQueue = new QueueKey("other");
        await InsertJobAsync(queue: QueueKey.Default, status: AtomizerJobStatus.Pending);
        await InsertJobAsync(queue: otherQueue, status: AtomizerJobStatus.Pending);
        await InsertJobAsync(queue: QueueKey.Default, status: AtomizerJobStatus.Processing);

        var result = await _sut.GetJobsAsync(
            new JobQuery { QueueKey = QueueKey.Default, Statuses = [AtomizerJobStatus.Pending] },
            CancellationToken.None
        );

        result.TotalCount.Should().Be(1);
        result.Items[0].QueueKey.Should().Be(QueueKey.Default);
        result.Items[0].Status.Should().Be(AtomizerJobStatus.Pending);
    }

    [Fact]
    public async Task GetJobsAsync_WhenSkipExceedsTotalCount_ShouldReturnEmptyItems()
    {
        await InsertJobAsync();
        await InsertJobAsync();

        var result = await _sut.GetJobsAsync(new JobQuery { Skip = 10 }, CancellationToken.None);

        result.Items.Should().BeEmpty();
        result.TotalCount.Should().Be(2);
    }

    [Fact]
    public async Task GetJobsAsync_WhenTakeExceedsHardCeiling_ShouldCapAt500()
    {
        var result = await _sut.GetJobsAsync(new JobQuery { Take = 1000 }, CancellationToken.None);

        result.Take.Should().Be(500);
    }

    [Fact]
    public async Task GetJobsAsync_ShouldReturnJobsOrderedByCreatedAtDescending()
    {
        var job1 = await InsertJobAsync(createdAt: _now.AddSeconds(-2));
        var job2 = await InsertJobAsync(createdAt: _now.AddSeconds(-1));
        var job3 = await InsertJobAsync(createdAt: _now);

        var result = await _sut.GetJobsAsync(new JobQuery(), CancellationToken.None);

        result.Items[0].Id.Should().Be(job3.Id);
        result.Items[1].Id.Should().Be(job2.Id);
        result.Items[2].Id.Should().Be(job1.Id);
    }

    [Fact]
    public async Task GetQueueStatsAsync_ShouldGroupByQueueAndCountByStatus()
    {
        var queue2 = new QueueKey("queue2");

        await InsertJobAsync(queue: QueueKey.Default, status: AtomizerJobStatus.Pending);
        await InsertJobAsync(queue: QueueKey.Default, status: AtomizerJobStatus.Pending);
        await InsertJobAsync(queue: QueueKey.Default, status: AtomizerJobStatus.Processing);
        await InsertJobAsync(queue: queue2, status: AtomizerJobStatus.Completed);

        var result = await _sut.GetQueueStatsAsync(CancellationToken.None);

        var defaultStats = result.Single(s => s.QueueKey == QueueKey.Default);
        defaultStats.Pending.Should().Be(2);
        defaultStats.Processing.Should().Be(1);
        defaultStats.Completed.Should().Be(0);
        defaultStats.Failed.Should().Be(0);

        var queue2Stats = result.Single(s => s.QueueKey == queue2);
        queue2Stats.Completed.Should().Be(1);
    }

    [Fact]
    public async Task GetSchedulesAsync_ShouldReturnAllSchedules()
    {
        var schedule = AtomizerSchedule.Create(
            new JobKey("test-job"),
            QueueKey.Default,
            typeof(string),
            "{}",
            Schedule.Default,
            TimeZoneInfo.Utc,
            _now
        );
        await _inMemoryStorage.UpsertScheduleAsync(schedule, CancellationToken.None);

        var result = await _sut.GetSchedulesAsync(CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].JobKey.Should().Be(schedule.JobKey);
    }

    [Fact]
    public async Task GetActiveServersAsync_ShouldReturnAllRegisteredServers()
    {
        var server1 = new AtomizerActiveServer { InstanceId = "server-1", LastHeartbeatAt = _now };
        var server2 = new AtomizerActiveServer { InstanceId = "server-2", LastHeartbeatAt = _now.AddMinutes(-1) };

        await _inMemoryStorage.UpsertHeartbeatAsync(server1, CancellationToken.None);
        await _inMemoryStorage.UpsertHeartbeatAsync(server2, CancellationToken.None);

        var result = await _sut.GetActiveServersAsync(CancellationToken.None);

        result.Should().HaveCount(2);
        result.Should().Contain(s => s.InstanceId == "server-1");
        result.Should().Contain(s => s.InstanceId == "server-2");
    }
}
