using Atomizer.Core;
using Atomizer.Redis.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Atomizer.Redis.Tests.Dashboard;

[Collection(nameof(Storage.RedisStorageFixture))]
public sealed class RedisStorageMonitoringTests : IAsyncLifetime
{
    private readonly Storage.RedisStorageFixture _fixture;
    private readonly IAtomizerClock _clock = Substitute.For<IAtomizerClock>();
    private readonly DateTimeOffset _now = new DateTimeOffset(2024, 1, 15, 12, 0, 0, TimeSpan.Zero);
    private RedisStorage _sut = null!;

    public RedisStorageMonitoringTests(Storage.RedisStorageFixture fixture)
    {
        _fixture = fixture;
    }

    public async ValueTask InitializeAsync()
    {
        await _fixture.FlushAsync();
        _clock.UtcNow.Returns(_now);
        _sut = new RedisStorage(
            _fixture.Connection,
            new RedisJobStorageOptions { KeyPrefix = $"dashboard:{Guid.NewGuid():N}" },
            _clock,
            NullLogger<RedisStorage>.Instance
        );
    }

    public async ValueTask DisposeAsync()
    {
        await _fixture.FlushAsync();
    }

    [Fact]
    public async Task GetJobsAsync_WhenJobsExist_ShouldReturnPagedResultsOrderedByCreatedAtDescending()
    {
        await InsertJobAsync(createdAt: _now.AddMinutes(-3));
        await InsertJobAsync(createdAt: _now.AddMinutes(-2));
        await InsertJobAsync(createdAt: _now.AddMinutes(-1));

        var result = await _sut.GetJobsAsync(new JobQuery { Skip = 0, Take = 2 }, CancellationToken.None);

        result.TotalCount.Should().Be(3);
        result.Items.Should().HaveCount(2);
        result.Take.Should().Be(2);
        result.Items[0].CreatedAt.Should().Be(_now.AddMinutes(-1));
    }

    [Fact]
    public async Task GetJobsAsync_WhenFiltersAreApplied_ShouldReturnOnlyMatchingJobs()
    {
        var otherQueue = new QueueKey("other");
        await InsertJobAsync(queue: QueueKey.Default, status: AtomizerJobStatus.Pending, payloadType: typeof(string));
        await InsertJobAsync(queue: otherQueue, status: AtomizerJobStatus.Pending, payloadType: typeof(string));
        await InsertJobAsync(queue: QueueKey.Default, status: AtomizerJobStatus.Failed, payloadType: typeof(int));

        var result = await _sut.GetJobsAsync(
            new JobQuery
            {
                QueueKey = QueueKey.Default,
                Statuses = new[] { AtomizerJobStatus.Pending },
                PayloadTypeName = "STR",
            },
            CancellationToken.None
        );

        result.TotalCount.Should().Be(1);
        result.Items[0].QueueKey.Should().Be(QueueKey.Default);
        result.Items[0].Status.Should().Be(AtomizerJobStatus.Pending);
        result.Items[0].PayloadType.Should().Be(typeof(string));
    }

    [Fact]
    public async Task GetQueueStatsAsync_ShouldReturnCorrectCountsPerQueue()
    {
        var otherQueue = new QueueKey("other");
        await InsertJobAsync(queue: QueueKey.Default, status: AtomizerJobStatus.Pending);
        await InsertJobAsync(queue: QueueKey.Default, status: AtomizerJobStatus.Processing);
        await InsertJobAsync(queue: otherQueue, status: AtomizerJobStatus.Completed);

        var result = await _sut.GetQueueStatsAsync(CancellationToken.None);

        var defaultStats = result.Single(stats => stats.QueueKey == QueueKey.Default);
        defaultStats.Pending.Should().Be(1);
        defaultStats.Processing.Should().Be(1);

        var otherStats = result.Single(stats => stats.QueueKey == otherQueue);
        otherStats.Completed.Should().Be(1);
    }

    [Fact]
    public async Task GetJobStatusCountsAsync_WhenQueryHasStatusFilter_ShouldIgnoreStatusAndApplyOtherFilters()
    {
        var otherQueue = new QueueKey("other");
        await InsertJobAsync(queue: QueueKey.Default, status: AtomizerJobStatus.Pending);
        await InsertJobAsync(queue: QueueKey.Default, status: AtomizerJobStatus.Processing);
        await InsertJobAsync(queue: QueueKey.Default, status: AtomizerJobStatus.Failed);
        await InsertJobAsync(queue: otherQueue, status: AtomizerJobStatus.Completed);

        var result = await _sut.GetJobStatusCountsAsync(
            new JobQuery { QueueKey = QueueKey.Default, Statuses = new[] { AtomizerJobStatus.Pending } },
            CancellationToken.None
        );

        result.Pending.Should().Be(1);
        result.Processing.Should().Be(1);
        result.Completed.Should().Be(0);
        result.Failed.Should().Be(1);
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

        await _sut.UpsertScheduleAsync(schedule, CancellationToken.None);

        var result = await _sut.GetSchedulesAsync(CancellationToken.None);

        result.Should().ContainSingle(s => s.JobKey == schedule.JobKey);
    }

    [Fact]
    public async Task GetActiveServersAsync_ShouldReturnRegisteredServers()
    {
        await _sut.UpsertHeartbeatAsync(
            new AtomizerActiveServer { InstanceId = "server-1", LastHeartbeatAt = _now },
            CancellationToken.None
        );

        var result = await _sut.GetActiveServersAsync(CancellationToken.None);

        result.Should().ContainSingle(server => server.InstanceId == "server-1");
    }

    [Fact]
    public async Task GetJobByIdAsync_WhenJobHasErrors_ShouldReturnErrorHistory()
    {
        var job = await InsertJobAsync(status: AtomizerJobStatus.Failed);
        job.Errors.Add(AtomizerJobError.Create(job.Id, _now, 1, new InvalidOperationException("failed"), "server-1"));
        await _sut.UpdateJobsAsync(new[] { job }, CancellationToken.None);

        var result = await _sut.GetJobByIdAsync(job.Id, CancellationToken.None);

        result.Should().NotBeNull();
        result!.Errors.Should().ContainSingle(error => error.ErrorMessage == "failed");
    }

    private async Task<AtomizerJob> InsertJobAsync(
        QueueKey? queue = null,
        AtomizerJobStatus status = AtomizerJobStatus.Pending,
        Type? payloadType = null,
        DateTimeOffset? createdAt = null
    )
    {
        var created = createdAt ?? _now;
        var job = AtomizerJob.Create(queue ?? QueueKey.Default, payloadType ?? typeof(object), "{}", created, created);

        await _sut.InsertAsync(job, CancellationToken.None);

        if (status == AtomizerJobStatus.Processing)
        {
            var leaseToken = new LeaseToken($"server-1:*:{job.QueueKey.Key}:*:{Guid.NewGuid()}");
            job.Lease(leaseToken, _now, TimeSpan.FromMinutes(5));
            await _sut.UpdateJobsAsync(new[] { job }, CancellationToken.None);
        }
        else if (status == AtomizerJobStatus.Completed)
        {
            var leaseToken = new LeaseToken($"server-1:*:{job.QueueKey.Key}:*:{Guid.NewGuid()}");
            job.Lease(leaseToken, _now, TimeSpan.FromMinutes(5));
            job.Attempt();
            job.MarkAsCompleted(_now);
            await _sut.UpdateJobsAsync(new[] { job }, CancellationToken.None);
        }
        else if (status == AtomizerJobStatus.Failed)
        {
            var leaseToken = new LeaseToken($"server-1:*:{job.QueueKey.Key}:*:{Guid.NewGuid()}");
            job.Lease(leaseToken, _now, TimeSpan.FromMinutes(5));
            job.Attempt();
            job.MarkAsFailed(_now);
            await _sut.UpdateJobsAsync(new[] { job }, CancellationToken.None);
        }

        return job;
    }
}
